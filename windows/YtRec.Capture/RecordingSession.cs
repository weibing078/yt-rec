using System.Diagnostics;
using System.Text;
using Vortice.Direct3D11;
using Windows.Foundation.Metadata;
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
    private readonly IntPtr _hwnd;
    private readonly AudioLoopbackCapture _audio;
    private readonly string _ffmpegPath;
    private readonly string _segmentsDir;
    private readonly string _audioPcmPath;
    private readonly int _fps;
    private int _capW, _capH;

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
    private bool _setup;            // first-frame crop/buffer setup done (needed by preview AND recording)
    private volatile bool _writing; // false = preview only (no file); true = recording to ffmpeg

    public Action<byte[], int, int>? OnPreviewFrame { get; set; }
    // Mirror every 2nd captured frame (~15 fps at 30 fps source). The preview is now downscaled before it
    // reaches the UI (PreviewScaler), so a higher cadence is cheap and the viewfinder looks smooth, not choppy.
    public int PreviewEveryNthFrame { get; set; } = 2;
    private int _previewCounter;

    /// <summary>The deterministic output size (content-driven, from <see cref="YtRec.Core.CaptureGeometry"/>).
    /// The captured (cropped) video region is scaled+padded to exactly this, so the file is screen/DPI-
    /// independent. Null → output the captured size as-is (even-trimmed).</summary>
    public (int W, int H)? TargetSize { get; set; }
    private int _inW, _inH;

    /// <summary>Crop (fractions of the captured window) to the INLINE video region reported by the page. Keeping
    /// the video inline (not fullscreen-filled) is what makes it composite into the WGC-capturable window
    /// surface instead of a GPU overlay WGC renders black; the crop drops the surrounding page chrome.</summary>
    public (double X, double Y, double W, double H)? CropFrac { get; set; }
    private int _cropX, _cropY;

    public RecordingSession(IntPtr captureHwnd, AudioLoopbackCapture audio,
        string ffmpegPath, string segmentsDir, string audioPcmPath, int fps = 30)
    {
        _hwnd = captureHwnd;
        _audio = audio;
        _ffmpegPath = ffmpegPath;
        _segmentsDir = segmentsDir;
        _audioPcmPath = audioPcmPath;
        _fps = fps;
    }

    /// <summary>Begin WGC capture in PREVIEW mode: frames are read back and mirrored to
    /// <see cref="OnPreviewFrame"/> so the user can watch and rewind, but NOTHING is written to disk until
    /// <see cref="BeginWriting"/>. For record-now, the caller invokes Start() then BeginWriting() back-to-back.</summary>
    public void Start()
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("WGC not supported on this machine");

        Directory.CreateDirectory(_segmentsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_audioPcmPath)!);
        _awake = new SleepPrevention(keepDisplayOn: true);

        // Window capture of the Win32-hosted WebView2 (a real windowed HWND, unlike the visual-hosted WinUI
        // WebView2 control). WGC window-capture keeps working when the window is occluded/in the background —
        // that's what lets the user keep working while it records (mac §4/§5), no monitor-wide capture needed.
        var item = CaptureInterop.CreateForWindow(_hwnd);
        (_d3d, var device) = Direct3D11Helper.CreateDevice();
        _context = _d3d.ImmediateContext;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _pool.CreateCaptureSession(item);
        TryDisableBorder(_session); // no yellow capture border — it would be drawn into the whole-window frame
        _pool.FrameArrived += OnFrame;
        _session.StartCapture();
    }

    /// <summary>Switch from preview to RECORDING: open the audio sink, start loopback, and let frames flow to
    /// ffmpeg from the current player position. The session-gate then anchors the writer to the first audio
    /// sample (mac §2), so any pre-audio frames are dropped cleanly. Idempotent.</summary>
    public void BeginWriting()
    {
        lock (_lock)
        {
            if (_writing || _stopped) return;
            _audioFile = new FileStream(_audioPcmPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            _audio.Start(OnAudio);
            _writing = true;
        }
        // Safety (mac §8 audio-decision window): if no audio sample arrives in 8 s, open the gate anyway so
        // video still records (video-only survival) rather than dropping every frame forever.
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            if (!_stopped && !_gate.AudioStarted) _gate.OnAudioSample(0);
        });
    }

    /// <summary>Acquire borderless-capture access once before recording (Win11; auto-granted for desktop apps).
    /// Await this before <see cref="Start"/> so <see cref="TryDisableBorder"/> can actually drop the border.</summary>
    public static async Task RequestBorderlessAsync()
    {
        try
        {
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
        }
        catch { /* older build or denied — border stays; the opaque lid still hides it on screen */ }
    }

    private static void TryDisableBorder(GraphicsCaptureSession session)
    {
        try
        {
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                session.IsBorderRequired = false;
        }
        catch { /* older build (pre-Win11) or access not granted — harmless, border just stays */ }
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
            int w = (int)desc.Width, h = (int)desc.Height; // captured window size

            if (!_setup)
            {
                _capW = w; _capH = h;
                if (CropFrac is (double fx, double fy, double fw, double fh))
                {
                    _cropX = Math.Clamp((int)(fx * w), 0, w - 2);
                    _cropY = Math.Clamp((int)(fy * h), 0, h - 2);
                    _inW = Math.Clamp((int)(fw * w), 2, w - _cropX) & ~1;
                    _inH = Math.Clamp((int)(fh * h), 2, h - _cropY) & ~1;
                }
                else
                {
                    _cropX = 0; _cropY = 0;
                    _inW = w & ~1; _inH = h & ~1;   // even input for yuv420p (drop the odd last row/col)
                }
                (_result.Width, _result.Height) = TargetSize is { } t ? (t.W, t.H) : (_inW, _inH);
                _staging = _d3d!.CreateTexture2D(FrameReadback.StagingDesc(w, h, desc.Format));
                _frameBuf = new byte[_inW * _inH * 4];
                _setup = true;
            }
            if (w != _capW || h != _capH) return; // window resized mid-session — skip the odd frame

            FrameReadback.CopyCropToBuffer(_context!, _staging!, src, _frameBuf!, _cropX, _cropY, _inW, _inH);

            // Mirror to the viewfinder in BOTH preview and recording — this is exactly what the user watches
            // while rewinding to the start point. Downscale to a small box first (PreviewScaler): the full-res
            // crop is overkill for a ~360 px monitor and pushing it whole made the on-screen preview lag. The
            // downscale also produces the fresh detached buffer the UI thread keeps (no extra copy needed).
            if (OnPreviewFrame is { } preview && ++_previewCounter % PreviewEveryNthFrame == 0)
            {
                var (pw, ph) = PreviewScaler.FitBox(_inW, _inH);
                preview(PreviewScaler.DownscaleBgra(_frameBuf!, _inW, _inH, pw, ph), pw, ph);
            }

            if (!_writing) return; // preview only — nothing is written until BeginWriting()

            if (_ff is null) StartVideoFfmpeg(_inW, _inH, _result.Width, _result.Height);

            // Session-gate (mac §2): drop video until the first audio sample has arrived, then accept all.
            // We gate on "audio started" (a boolean), NOT a cross-clock tick compare — the audio QPC and the
            // WGC SystemRelativeTime don't share an epoch, so comparing them dropped every frame.
            if (!_gate.AudioStarted)
            {
                _result.VideoFramesDropped++;
                return;
            }

            // Real-time CFR pacing (clock starts at the first written frame): emit the latest frame as many
            // times as wall-clock says are due so the file plays at true speed.
            _videoClock ??= Stopwatch.StartNew();
            long target = (long)(_videoClock.Elapsed.TotalSeconds * _fps);
            while (_result.VideoFrames <= target)
            {
                try { _videoStdin!.Write(_frameBuf!, 0, _frameBuf!.Length); _result.VideoFrames++; }
                catch (IOException) { break; } // ffmpeg gone
            }
        }
    }

    private void StartVideoFfmpeg(int inW, int inH, int outW, int outH)
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
        Add("-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{inW}x{inH}", "-framerate", _fps.ToString(), "-i", "pipe:0");
        Add("-an", "-vf", ContinuousRecorder.ScalePadFilter(outW, outH));
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
        if (_writing) _audio.Stop();   // only started in BeginWriting(); a cancelled preview never started it
        if (_audioFile is not null) { try { _audioFile.Flush(); _audioFile.Dispose(); } catch { } _audioFile = null; }

        if (_videoStdin is not null) { try { _videoStdin.Flush(); } catch { } _videoStdin.Close(); }
        if (_ff is not null)
        {
            await _ff.WaitForExitAsync();
            _result.FfmpegExitCode = _ff.ExitCode;
            if (_ff.ExitCode != 0) _result.Error = $"video ffmpeg exit {_ff.ExitCode}: {_stderr}".Trim();
        }
        else if (_writing) _result.Error = "no video frames arrived"; // (a cancelled preview legitimately has none)

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
