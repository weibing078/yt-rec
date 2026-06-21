using System.Diagnostics;
using System.Text;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using YtRec.Core;

namespace YtRec.Capture;

public sealed class SessionResult
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int VideoFrames { get; set; }
    public int VideoFramesDropped { get; set; } // pre-anchor frames dropped by the session-gate
    public long AudioBytes { get; set; }
    public int? FfmpegExitCode { get; set; }
    public string? Error { get; set; }
}

/// <summary>The live A/V recorder the app drives. WGC video is read back to BGRA and piped to a single ffmpeg
/// that writes crash-resistant HLS fMP4 segments; loopback audio (PCM) is written straight to a sibling file.
/// Both are anchored to the first audio sample by a <see cref="SessionGate"/> (the mac §2 fix that stops the
/// opening fragment from being poisoned), so they line up. Finalize/recover muxes them with
/// <see cref="SegmentReassembler"/>. (Recording the two streams separately and muxing at the end avoids the
/// 2-input live-pipe deadlock that a single muxing ffmpeg hits.) The machine is kept awake for the session.</summary>
public sealed class RecordingSession : IDisposable
{
    private readonly IntPtr _monitor;
    private readonly int _cropX, _cropY, _cropW, _cropH;
    private readonly AudioLoopbackCapture _audio;
    private readonly string _ffmpegPath;
    private readonly string _segmentsDir;
    private readonly string _audioPcmPath;
    private readonly int _fps;
    private int _monW, _monH;

    private readonly SessionGate _gate = new();
    private readonly SessionResult _result = new();
    private readonly object _lock = new();

    private SleepPrevention? _awake;
    private ID3D11Device? _d3d;
    private ID3D11DeviceContext? _context;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    private byte[]? _frameBuf;
    private Stopwatch? _videoClock;

    private Process? _ff;
    private Stream? _videoStdin;
    private FileStream? _audioFile;
    private readonly StringBuilder _stderr = new();
    private volatile bool _stopped;

    public Action<byte[], int, int>? OnPreviewFrame { get; set; }
    public int PreviewEveryNthFrame { get; set; } = 5;
    private int _previewCounter;

    public RecordingSession(IntPtr monitor, (int X, int Y, int W, int H) crop, AudioLoopbackCapture audio,
        string ffmpegPath, string segmentsDir, string audioPcmPath, int fps = 30)
    {
        _monitor = monitor;
        (_cropX, _cropY, _cropW, _cropH) = crop;
        _audio = audio;
        _ffmpegPath = ffmpegPath;
        _segmentsDir = segmentsDir;
        _audioPcmPath = audioPcmPath;
        _fps = fps;
    }

    public void Start()
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("WGC not supported on this machine");

        Directory.CreateDirectory(_segmentsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_audioPcmPath)!);
        _awake = new SleepPrevention(keepDisplayOn: true);

        // Monitor capture (not window capture): WGC window-capture can't see a WebView2's video swapchain,
        // so we capture the whole monitor and crop to the player window's rectangle.
        var item = CaptureInterop.CreateForMonitor(_monitor);
        (_d3d, var device) = Direct3D11Helper.CreateDevice();
        _context = _d3d.ImmediateContext;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _pool.CreateCaptureSession(item);
        _pool.FrameArrived += OnFrame;

        _audioFile = new FileStream(_audioPcmPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        _audio.Start(OnAudio);
        _session.StartCapture();

        // Safety (mac §8 audio-decision window): if no audio sample arrives in 8 s, open the gate anyway so
        // video still records (video-only survival) rather than dropping every frame forever.
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            if (!_stopped && !_gate.AudioStarted) _gate.OnAudioSample(0);
        });
    }

    private void OnAudio(byte[] buf, int count, long qpcTicks)
    {
        if (_stopped) return;
        _gate.OnAudioSample(qpcTicks);
        try { _audioFile?.Write(buf, 0, count); _result.AudioBytes += count; }
        catch { /* file closing on stop */ }
    }

    private void OnFrame(Direct3D11CaptureFramePool sender, object _)
    {
        lock (_lock)
        {
            if (_stopped) return;
            using var frame = sender.TryGetNextFrame();
            if (frame is null) return;

            using var src = FrameReadback.SurfaceToTexture(frame.Surface);
            var desc = src.Description;
            int w = (int)desc.Width, h = (int)desc.Height; // full monitor size

            if (_ff is null)
            {
                _monW = w; _monH = h;
                _result.Width = Math.Min(_cropW, w - _cropX) & ~1;   // crop clamped to the monitor, even dims
                _result.Height = Math.Min(_cropH, h - _cropY) & ~1;
                _staging = _d3d!.CreateTexture2D(FrameReadback.StagingDesc(w, h, desc.Format));
                _frameBuf = new byte[_result.Width * _result.Height * 4];
                StartVideoFfmpeg(_result.Width, _result.Height);
            }
            if (w != _monW || h != _monH) return; // monitor resolution changed mid-record

            // Session-gate (mac §2): drop video until the first audio sample has arrived, then accept all.
            // We gate on "audio started" (a boolean), NOT a cross-clock tick compare — the audio QPC and the
            // WGC SystemRelativeTime don't share an epoch, so comparing them dropped every frame.
            if (!_gate.AudioStarted)
            {
                _result.VideoFramesDropped++;
                return;
            }

            FrameReadback.CopyCropToBuffer(_context!, _staging!, src, _frameBuf!, _cropX, _cropY, _result.Width, _result.Height);

            // Real-time CFR pacing (clock starts at the first post-gate frame): emit the latest frame as many
            // times as wall-clock says are due so the file plays at true speed.
            _videoClock ??= Stopwatch.StartNew();
            long target = (long)(_videoClock.Elapsed.TotalSeconds * _fps);
            while (_result.VideoFrames <= target)
            {
                try { _videoStdin!.Write(_frameBuf!, 0, _frameBuf!.Length); _result.VideoFrames++; }
                catch (IOException) { break; } // ffmpeg gone
            }

            if (OnPreviewFrame is { } preview && ++_previewCounter % PreviewEveryNthFrame == 0)
            {
                var copy = new byte[_frameBuf!.Length];
                Array.Copy(_frameBuf, copy, copy.Length);
                preview(copy, w, h);
            }
        }
    }

    private void StartVideoFfmpeg(int w, int h)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true, // no console window — it would pop over the captured player and get recorded
            // The HLS muxer writes hls_fmp4_init_filename relative to its CWD (not the playlist dir), so run
            // ffmpeg IN the segments dir and use bare output names — otherwise it tries to write seg_init.mp4
            // into the app's (read-only) working dir → "Permission denied".
            WorkingDirectory = _segmentsDir,
        };
        void Add(params string[] xs) { foreach (var x in xs) psi.ArgumentList.Add(x); }
        Add("-hide_banner", "-loglevel", "error", "-y");
        Add("-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{w}x{h}", "-framerate", _fps.ToString(), "-i", "pipe:0");
        Add("-an", "-vf", ContinuousRecorder.EvenDimsFilter);
        Add("-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p");
        Add("-f", "hls", "-hls_time", "2", "-hls_list_size", "0",
            "-hls_segment_type", "fmp4",
            "-hls_flags", "independent_segments+temp_file",
            "-hls_fmp4_init_filename", SegmentReassembler.InitName,
            "-hls_segment_filename", SegmentReassembler.SegmentPattern,
            "index.m3u8");

        _ff = Process.Start(psi)!;
        _ff.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_stderr) _stderr.AppendLine(e.Data); };
        _ff.BeginErrorReadLine();
        _videoStdin = _ff.StandardInput.BaseStream;
    }

    /// <summary>Stop capture and flush. The caller then muxes segments + audio.pcm into a clean MP4 via
    /// <see cref="SegmentReassembler.ReassembleAsync"/>.</summary>
    public async Task<SessionResult> StopAsync()
    {
        lock (_lock)
        {
            if (_stopped) return _result;
            _stopped = true;
        }

        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _audio.Stop();
        if (_audioFile is not null) { try { _audioFile.Flush(); _audioFile.Dispose(); } catch { } _audioFile = null; }

        if (_videoStdin is not null) { try { _videoStdin.Flush(); } catch { } _videoStdin.Close(); }
        if (_ff is not null)
        {
            await _ff.WaitForExitAsync();
            _result.FfmpegExitCode = _ff.ExitCode;
            if (_ff.ExitCode != 0) _result.Error = $"video ffmpeg exit {_ff.ExitCode}: {_stderr}".Trim();
        }
        else _result.Error = "no video frames arrived";

        return _result;
    }

    public void Dispose()
    {
        if (!_stopped) { try { StopAsync().GetAwaiter().GetResult(); } catch { } }
        _ff?.Dispose();
        _staging?.Dispose();
        _context?.Dispose();
        _d3d?.Dispose();
        _audio.Dispose();
        _awake?.Dispose();
    }
}
