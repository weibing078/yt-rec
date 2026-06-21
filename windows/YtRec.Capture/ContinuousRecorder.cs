using System.Diagnostics;
using System.Text;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using YtRec.Core;

namespace YtRec.Capture;

public sealed class RecordResult
{
    public bool Supported { get; init; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FramesWritten { get; set; }
    public long BytesWritten { get; set; }
    public int? FfmpegExitCode { get; set; }
    public string? Error { get; set; }
}

/// <summary>Phase 2 (step 1): product-grade continuous recorder. Each WGC frame is read back from the
/// GPU via a CPU-readable D3D11 staging texture and the raw BGRA is piped straight into bundled ffmpeg
/// (-f rawvideo). Two output modes share one capture loop:
///   • <see cref="RecordAsync"/> — a single fragmented MP4 (+frag_keyframe+empty_moov), simplest path.
///   • <see cref="RecordSegmentedAsync"/> — HLS fMP4 (seg_init.mp4 + seg_%05d.m4s) for crash-resistance:
///     a kill-9 loses only the in-progress 2 s segment; finalize/recover via <c>SegmentReassembler</c>.
/// Video only — audio (WASAPI process-loopback) is layered in by the host orchestrator.</summary>
public static class ContinuousRecorder
{
    // A captured window can be any size; yuv420p/libx264 require even dimensions, so crop the odd
    // last row/column away before encoding (drops at most 1px). Found on real hardware: a 1115×628 window.
    internal const string EvenDimsFilter = "crop=trunc(iw/2)*2:trunc(ih/2)*2";

    /// <summary>Record <paramref name="hwnd"/> into a single fragmented MP4 at <paramref name="outPath"/>.</summary>
    public static Task<RecordResult> RecordAsync(IntPtr hwnd, string ffmpegPath, string outPath, int fps, int seconds)
        => CaptureToFfmpegAsync(hwnd, ffmpegPath, fps, seconds, (_, _) => new[]
        {
            "-vf", EvenDimsFilter,
            "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
            "-movflags", "+frag_keyframe+empty_moov+default_base_moof",
            outPath,
        });

    /// <summary>Record <paramref name="hwnd"/> into crash-resistant HLS fMP4 segments under
    /// <paramref name="segDir"/> (created if needed): seg_init.mp4 + seg_00000.m4s … every ~2 s.</summary>
    public static Task<RecordResult> RecordSegmentedAsync(IntPtr hwnd, string ffmpegPath, string segDir, int fps, int seconds)
    {
        Directory.CreateDirectory(segDir);
        return CaptureToFfmpegAsync(hwnd, ffmpegPath, fps, seconds, (_, _) => new[]
        {
            "-vf", EvenDimsFilter,
            "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
            "-f", "hls", "-hls_time", "2", "-hls_list_size", "0",
            "-hls_segment_type", "fmp4",
            "-hls_flags", "independent_segments+temp_file",
            "-hls_fmp4_init_filename", SegmentReassembler.InitName,
            "-hls_segment_filename", Path.Combine(segDir, SegmentReassembler.SegmentPattern),
            Path.Combine(segDir, "index.m3u8"),
        });
    }

    /// <summary>The shared loop: WGC → staging readback → BGRA into ffmpeg stdin. <paramref name="outputArgs"/>
    /// supplies the post-input ffmpeg args (codec + muxer) once the frame size is known.</summary>
    private static async Task<RecordResult> CaptureToFfmpegAsync(
        IntPtr hwnd, string ffmpegPath, int fps, int seconds,
        Func<int, int, IReadOnlyList<string>> outputArgs)
    {
        if (!GraphicsCaptureSession.IsSupported())
            return new RecordResult { Supported = false, Error = "WGC not supported" };

        var result = new RecordResult { Supported = true };

        GraphicsCaptureItem item;
        try { item = CaptureInterop.CreateForWindow(hwnd); }
        catch (Exception e) { result.Error = "item: " + e.Message; return result; }

        var (d3d, device) = Direct3D11Helper.CreateDevice();
        var context = d3d.ImmediateContext;
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        var session = pool.CreateCaptureSession(item);

        var gate = new object();
        Process? ff = null;
        Stream? stdin = null;
        var stderr = new StringBuilder();
        ID3D11Texture2D? staging = null;
        byte[]? frameBuf = null;
        Stopwatch? clock = null;
        var stopped = false;

        pool.FrameArrived += (sender, _) =>
        {
            lock (gate)
            {
                if (stopped) return;
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;

                using var src = FrameReadback.SurfaceToTexture(frame.Surface);
                var desc = src.Description;
                int w = (int)desc.Width, h = (int)desc.Height;

                if (ff is null)
                {
                    result.Width = w;
                    result.Height = h;
                    staging = d3d.CreateTexture2D(FrameReadback.StagingDesc(w, h, desc.Format));
                    frameBuf = new byte[w * h * 4];
                    (ff, _) = StartFfmpeg(ffmpegPath, w, h, fps, outputArgs(w, h), stderr);
                    stdin = ff.StandardInput.BaseStream;
                    clock = Stopwatch.StartNew();
                }

                // Window may resize mid-capture; the frame pool keeps the original size, so skip
                // any frame whose texture no longer matches the staging texture we sized ffmpeg to.
                if (w != result.Width || h != result.Height) return;

                FrameReadback.CopyToBuffer(context, staging!, src, frameBuf!, w, h);

                // Real-time CFR pacing: WGC delivers frames at the window's (variable) render rate, but we
                // told ffmpeg a fixed -framerate. Emit this frame as many times as wall-clock says are due
                // (duplicating when the window is idle, dropping when it renders faster than fps) so the
                // output's duration matches real time instead of running fast/slow.
                long target = (long)(clock!.Elapsed.TotalSeconds * fps);
                while (result.FramesWritten <= target)
                {
                    try { stdin!.Write(frameBuf!, 0, frameBuf!.Length); }
                    catch (IOException) { return; } // ffmpeg gone — stop feeding
                    result.FramesWritten++;
                    result.BytesWritten += frameBuf!.Length;
                }
            }
        };

        session.StartCapture();
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        Stream? toClose;
        lock (gate) { stopped = true; toClose = stdin; stdin = null; }

        session.Dispose();
        pool.Dispose();

        if (toClose is not null)
        {
            try { toClose.Flush(); } catch { /* ffmpeg may have exited */ }
            toClose.Close();
        }

        if (ff is not null)
        {
            await ff.WaitForExitAsync();
            result.FfmpegExitCode = ff.ExitCode;
            if (ff.ExitCode != 0)
                result.Error = $"ffmpeg exit {ff.ExitCode}: {stderr}".Trim();
            ff.Dispose();
        }
        else
        {
            result.Error = "no frames arrived (window minimized, occluded without anti-occlusion flags, or static)";
        }

        staging?.Dispose();
        context.Dispose();
        d3d.Dispose();
        return result;
    }

    private static (Process, StringBuilder) StartFfmpeg(
        string ffmpegPath, int w, int h, int fps, IReadOnlyList<string> outputArgs, StringBuilder stderr)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pixel_format", "bgra",
            "-video_size", $"{w}x{h}", "-framerate", fps.ToString(),
            "-i", "pipe:0",
            "-an",
        }) psi.ArgumentList.Add(a);
        foreach (var a in outputArgs) psi.ArgumentList.Add(a);

        var p = Process.Start(psi)!;
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        return (p, stderr);
    }
}
