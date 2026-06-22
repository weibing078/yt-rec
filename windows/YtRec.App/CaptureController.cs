using Microsoft.UI.Dispatching;
using YtRec.Capture;
using YtRec.Core;

namespace YtRec.App;

/// <summary>Drives one screen side-record on the UI thread in two phases. <see cref="PrepareAsync"/> loads the
/// off-screen player, auto-skips ads + waits for real content, acquires loopback audio, sizes the capture, and
/// starts the <see cref="RecordingSession"/> in PREVIEW (live frames mirror to the floating monitor, nothing is
/// written); <see cref="BeginRecording"/> then writes from the current player position. <see cref="StartAsync"/>
/// does both back-to-back for record-now. Finalize muxes the segments into a clean MP4; the ViewModel listens to
/// the events.</summary>
public sealed class CaptureController
{
    public event Action<string>? Status;
    public event Action<string>? Finished;   // output mp4 path
    public event Action<string>? Failed;      // error message

    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly string _ffmpegPath;

    private Win32PlayerHost? _player;
    private MonitorWindow? _monitor;
    private RecordingSession? _session;
    private string _segmentsDir = "";
    private string _audioPcmPath = "";
    private string _outputPath = "";
    private bool _previewing;   // session started in preview; not yet writing to a file
    private bool _stopping;

    public bool IsRecording { get; private set; }
    /// <summary>True while the live preview is up and the user can rewind, before recording has begun.</summary>
    public bool IsPreviewing => _previewing && !IsRecording;

    public CaptureController(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    /// <summary>Record-now: prepare the live preview then immediately begin writing (autorecord + plain 側錄).</summary>
    public async Task StartAsync(string url, string jobDir, string title, int fps = 30, int quality = 1080)
    {
        await PrepareAsync(url, jobDir, title, fps, quality);
        BeginRecording();
    }

    /// <summary>Phase 1: load the off-screen player, auto-skip ads + wait for real content, acquire audio, size
    /// the capture, and start the session in PREVIEW. The monitor then shows live frames and the user can rewind;
    /// call <see cref="BeginRecording"/> to start writing or <see cref="CancelPreview"/> to abort with no file.</summary>
    public async Task PrepareAsync(string url, string jobDir, string title, int fps = 30, int quality = 1080)
    {
        var watchUrl = PlayerAssets.WatchUrlFrom(url) ?? throw new InvalidOperationException("不是有效的 YouTube 影片網址");
        _segmentsDir = SegmentReassembler.SegmentsDir(jobDir);
        _audioPcmPath = SegmentReassembler.AudioPath(jobDir);
        _outputPath = OutputPaths.SideRecordOutput(jobDir);
        Directory.CreateDirectory(_segmentsDir);

        Status?.Invoke("開啟播放器…");
        _player = new Win32PlayerHost();
        await _player.LoadAsync(watchUrl, Path.Combine(jobDir, ".work", "webview2"));

        // Wait for REAL content (never an ad) to be rolling before we record. The injected script auto-skips
        // skippable ads and reports ContentReady only when actual content plays — so a non-Premium user's
        // pre-roll ad is skipped/waited out and never lands in the file. Cap the wait so a detection miss can't
        // hang forever; the session-gate still anchors the writer to the first audio sample (mac §8).
        Status?.Invoke("等待正片開始（自動略過廣告）…");
        var contentDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (!_player.ContentReady && DateTime.UtcNow < contentDeadline)
        {
            if (_player.AdShowing) Status?.Invoke("略過廣告中…");
            await Task.Delay(250);
        }
        // Brief audio-decode settle once content is actually playing (mac §8).
        await Task.Delay(1500);

        var audio = MakeAudio(_player.BrowserProcessId);

        // The floating viewfinder is optional — a failure to open it must never abort the recording.
        try
        {
            _monitor = new MonitorWindow(title);
            _monitor.StopRequested += () => _ = StopAsync();
            _monitor.Activate();
        }
        catch (Exception ex)
        {
            _monitor = null;
            Status?.Invoke($"監看小窗開啟失敗，仍繼續錄製：{ex.Message} [HRESULT=0x{ex.HResult:X8}] inner={ex.InnerException?.Message}");
        }

        // Content-driven output geometry (screen/DPI-independent): from the source video dims pick
        // landscape/portrait + the exact output size, size the on-screen capture window to the largest box of
        // that aspect that fits the screen, and have the recorder scale+pad to the exact target. The page CSS
        // fills the player to the whole window, so we capture the window edge-to-edge (no crop, no null-rect race).
        var dims = _player.VideoDims;
        var target = dims is { } dd
            ? CaptureGeometry.OutputSize(dd.W, dd.H, quality)
            : CaptureGeometry.OutputSize(1920, 1080, quality); // no report → assume landscape 1080p
        var (screenW, screenH) = Win32PlayerHost.PrimaryScreenPixels();
        var win = CaptureGeometry.FitWindow(target, screenW, screenH);
        _player.ClearVideoRect();           // discard the pre-resize rect
        _player.Resize(win.Width, win.Height);
        // Wait for a FRESH crop rect for the new (possibly portrait) layout — a stale/null rect falls back to
        // a whole-window capture that includes the watch page's letterboxing (pillarbox on vertical).
        for (int i = 0; i < 20 && _player.VideoRectFrac is null; i++) await Task.Delay(200);

        // The player sits 99.99% off-screen (Win32PlayerHost.Resize) and is click-through, so the user never
        // sees or touches it — the Mac off-screen experience, no opaque lid. Verified on real Win11 that WGC
        // still captures the full window's surface.

        // Window-capture the Win32-hosted WebView2 (works occluded/in the background), crop to the inline video
        // region (keeps the video inline → composites into the capturable surface, not a black overlay), then
        // the recorder scales+pads that crop to the exact target so the output is a clean, content-driven size.
        _session = new RecordingSession(_player.Hwnd, audio, _ffmpegPath, _segmentsDir, _audioPcmPath, fps)
        {
            OnPreviewFrame = (buf, w, h) => _monitor?.UpdatePreview(buf, w, h),
            TargetSize = (target.Width, target.Height),
            CropFrac = _player.VideoRectFrac,
        };
        // Stream end: stop if recording, else just tear the preview down (no file).
        _player.Ended += () => _ui.TryEnqueue(() => { if (IsRecording) _ = StopAsync(); else CancelPreview(); });

        await RecordingSession.RequestBorderlessAsync(); // drop the yellow WGC border before capture
        _session.Start();           // PREVIEW: frames mirror to the monitor, nothing is written yet
        _previewing = true;
        _monitor?.SetStatus("預覽中 · 倒帶到要開始錄的時間點");
        Status?.Invoke("預覽中");
    }

    /// <summary>Phase 2: switch the live preview into recording from the current player position.</summary>
    public void BeginRecording()
    {
        if (_session is null || IsRecording) return;
        _session.BeginWriting();
        _previewing = false;
        IsRecording = true;
        _monitor?.SetStatus(AudioCapability.IsolatedAudioSupported(OsBuild)
            ? "錄製中（只錄這個串流的聲音）"
            : "錄製中（此電腦會錄到全系統聲音）");
        Status?.Invoke("錄製中");
    }

    /// <summary>Seek the live preview to <paramref name="behindSec"/> behind the live edge (rewind scrubber).</summary>
    public async Task SeekToBehindAsync(double behindSec)
    {
        if (_player is not null) await _player.SeekBehindAsync(behindSec);
    }

    /// <summary>Read the player's live position (behind-live + DVR window) for the scrubber; null if not a live
    /// DVR stream yet.</summary>
    public async Task<DvrProgress?> ProgressAsync()
        => DvrScrubber.ParseProgress(_player is null ? null : await _player.ProgressStateAsync());

    /// <summary>Abort a preview that never started recording — tear everything down with no file produced.</summary>
    public void CancelPreview()
    {
        if (IsRecording || _stopping) return;
        _stopping = true;
        try { _ = _session?.StopAsync(); } catch { }   // disposes the WGC capture; nothing to reassemble
        CloseWindows();
        _session = null;
        _previewing = false;
        _stopping = false;
    }

    private static int OsBuild => Environment.OSVersion.Version.Build;

    private static AudioLoopbackCapture MakeAudio(uint browserPid)
    {
        // Win11 → per-process isolation (target the WebView2 browser tree); Win10 → system-audio fallback.
        // Actual COM acquisition is deferred to AudioLoopbackCapture.Start (on its dedicated MTA thread).
        var source = AudioCapability.ModeForBuild(OsBuild) == AudioMode.PerProcessLoopback
            ? AudioLoopbackCapture.Source.ProcessLoopback
            : AudioLoopbackCapture.Source.SystemLoopback;
        return new AudioLoopbackCapture(source, browserPid);
    }

    public async Task StopAsync()
    {
        if (_stopping || _session is null) return;
        _stopping = true;
        IsRecording = false;

        try
        {
            _monitor?.SetStatus("整理檔案中…");
            Status?.Invoke("整理檔案中…");
            var result = await _session.StopAsync();
            Status?.Invoke($"session: frames={result.VideoFrames} dropped={result.VideoFramesDropped} audioBytes={result.AudioBytes} ffmpegExit={result.FfmpegExitCode} {result.Error}");

            var (ok, err) = await SegmentReassembler.ReassembleAsync(
                _segmentsDir, _outputPath, _ffmpegPath, new ProcessRunner(), _audioPcmPath);

            CloseWindows();

            if (ok) Finished?.Invoke(_outputPath);
            else Failed?.Invoke(err ?? result.Error ?? "錄製失敗");
        }
        catch (Exception e)
        {
            CloseWindows();
            Failed?.Invoke(e.Message);
        }
        finally
        {
            _session?.Dispose();
            _session = null;
            _stopping = false;
        }
    }

    private void CloseWindows()
    {
        try { _monitor?.CloseMonitor(); } catch { }
        try { _player?.Close(); } catch { }
        _monitor = null;
        _player = null;
    }
}
