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
    public event Action<string>? Warning;     // non-fatal (recording continues) — e.g. audio device changed
    /// <summary>Preview was torn down with no file (monitor stop). The main window must leave "錄製中".</summary>
    public event Action<string>? PreviewDismissed;
    /// <summary>Latest "影片已結束" from the once-a-second snapshot. Not sticky: the next snapshot can clear it.</summary>
    public event Action<bool>? PreviewEndedChanged;

    public const string PreviewDismissedReason = "已取消監看，沒有開始錄影。";

    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly string _ffmpegPath;

    private Win32PlayerHost? _player;
    private MonitorWindow? _monitor;
    private RecordingSession? _session;
    private string _jobDir = "";
    private string _segmentsDir = "";
    private string _audioPcmPath = "";
    private string _outputPath = "";
    private bool _previewing;   // session started in preview; not yet writing to a file
    private bool _stopping;
    private DispatcherQueueTimer? _snapTimer;
    private PlayerSnapshot? _latestSnap;
    private DateTime? _candidateStart;
    private int _extendedAdSeconds;
    private string _anchorId = "";
    private string _lastSnapshotUi = "";
    private bool _snapshotStopping;
    private bool _audioDeviceLost; // Win10 loopback endpoint invalidated mid-record → finalize must keep full video
    private string? _faultMessage; // cause of a capture-callback fault, preferred over the reassembler's error

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
        _anchorId = YtUrl.VideoId(url) ?? "";
        _jobDir = jobDir;
        _audioDeviceLost = false;
        _segmentsDir = SegmentReassembler.SegmentsDir(jobDir);
        _audioPcmPath = SegmentReassembler.AudioPath(jobDir);
        Directory.CreateDirectory(_segmentsDir);

        try
        {
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
            // Win10 system-loopback only: if the default output endpoint is unplugged/switched mid-record the
            // audio silently dies — warn (video keeps recording), and remember it so finalize keeps the full
            // video instead of letting -shortest truncate it to the audio length. Fires on the audio MTA thread.
            audio.OnDeviceLost = () =>
            {
                _audioDeviceLost = true;
                Warning?.Invoke("系統音訊輸出裝置已變更，側錄聲音已從此刻中斷（畫面仍會完整錄完，之後的段落將沒有聲音）。");
            };

            // The floating viewfinder is optional — a failure to open it must never abort the recording.
            try
            {
                _monitor = new MonitorWindow(title);
                // Preview (not yet writing) → clean cancel with no file; recording → finalize. Keying on
                // IsRecording (not IsPreviewing) means a stop during the prep window also aborts cleanly instead
                // of running the reassembler on an empty segments dir and reporting a false "側錄失敗".
                _monitor.StopRequested += () => { if (IsRecording) _ = StopAsync(); else CancelPreview(); };
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
                // A throw inside the free-threaded WGC callback (GPU device-loss/TDR, hybrid-GPU switch, display
                // unplug, ffmpeg spawn failure) would otherwise crash the whole app. Marshal to the UI thread and
                // finalize/report cleanly (§3).
                OnError = msg => _ui.TryEnqueue(() => OnSessionError(msg)),
                TargetSize = (target.Width, target.Height),
                CropFrac = _player.VideoRectFrac,
            };
            // Stream end is a 20s candidate while recording. Preview ended does not tear the monitor down
            // (that used to leave the main window showing 「錄製中」 with no session underneath).
            _player.Snapshot += (ended, ad, content, id) =>
            {
                var snap = new PlayerSnapshot(ended, ad, content, id ?? "", DateTime.UtcNow);
                _ui.TryEnqueue(() => _latestSnap = snap);
            };

            await RecordingSession.RequestBorderlessAsync(); // drop the yellow WGC border before capture
            _session.Start();           // PREVIEW: frames mirror to the monitor, nothing is written yet
            _previewing = true;
            _monitor?.SetStatus("預覽中 · 倒帶到要開始錄的時間點");
            Status?.Invoke("預覽中");
            StartSnapshotTimer();
        }
        catch
        {
            // Any failure after the player came up (WebView2 missing, RequestBorderlessAsync/Start throwing, …)
            // must not leave the off-screen player alive: an invisible YouTube would keep playing sound with no
            // way to close it, and each retry would leak another WebView2 process. Tear everything down, then
            // rethrow for the ViewModel to surface.
            try { _ = _session?.StopAsync(); } catch { }
            CloseWindows();
            _session = null;
            _previewing = false;
            throw;
        }
    }

    /// <summary>Phase 2: switch the live preview into recording from the current player position.
    /// Returns false when the preview is already gone — the caller must say so instead of showing 「錄製中」.</summary>
    public bool BeginRecording()
    {
        if (!StreamEndGate.BeginRecordingSucceeds(_session is not null, _previewing, IsRecording)) return false;
        _session!.BeginWriting();
        _previewing = false;
        IsRecording = true;
        _monitor?.SetStatus(RecordingMonitorStatus());
        Status?.Invoke("錄製中");
        return true;
    }

    private string RecordingMonitorStatus() =>
        AudioCapability.IsolatedAudioSupported(OsBuild)
            ? "錄製中（只錄這個串流的聲音）"
            : "錄製中（此電腦會錄到全系統聲音）";

    private void StartSnapshotTimer()
    {
        StopSnapshotTimer();
        _snapTimer = _ui.CreateTimer();
        _snapTimer.Interval = TimeSpan.FromSeconds(1);
        _snapTimer.IsRepeating = true;
        _snapTimer.Tick += (_, _) => OnSnapshotTick();
        _snapTimer.Start();
    }

    private void StopSnapshotTimer()
    {
        _snapTimer?.Stop();
        _snapTimer = null;
    }

    /// <summary>First line: this controller is still previewing or recording. Otherwise the timer stops itself.</summary>
    private void OnSnapshotTick()
    {
        if (!IsPreviewing && !IsRecording)
        {
            StopSnapshotTimer();
            return;
        }
        var phase = IsRecording ? StreamPhase.Recording : StreamPhase.Preview;
        var decision = StreamEndGate.Evaluate(phase, _anchorId, _latestSnap, _candidateStart, _extendedAdSeconds, DateTime.UtcNow);
        _candidateStart = decision.CandidateStart;
        _extendedAdSeconds = decision.ExtendedAdSeconds;
        if (decision.LeaveUiAlone) return;
        if (decision.StopReason is { } why)
        {
            if (_snapshotStopping) return;
            _snapshotStopping = true;
            StopSnapshotTimer();
            ShowSnapshotStatus(why, why);
            _ = StopAsync();
            return;
        }
        if (decision.PreviewShowsEnded != _lastPreviewEnded)
        {
            _lastPreviewEnded = decision.PreviewShowsEnded;
            PreviewEndedChanged?.Invoke(decision.PreviewShowsEnded);
        }
        if (decision.CountdownSeconds is int sec)
            ShowSnapshotStatus($"影片即將結束，{sec} 秒後收工", $"影片即將結束，{sec} 秒後收工");
        else if (decision.PreviewShowsEnded)
            ShowSnapshotStatus("影片已結束", "影片已結束");
        else if (IsRecording)
            ShowSnapshotStatus("錄製中", RecordingMonitorStatus());
        else
            ShowSnapshotStatus("預覽中", "預覽中 · 倒帶到要開始錄的時間點");
    }

    private bool _lastPreviewEnded;

    /// <summary>Updates the main window and the monitor only when the text actually changes.</summary>
    private void ShowSnapshotStatus(string main, string monitor)
    {
        var key = main + "|" + monitor;
        if (key == _lastSnapshotUi) return;
        _lastSnapshotUi = key;
        Status?.Invoke(main);
        _monitor?.SetStatus(monitor);
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

    /// <summary>Abort a preview that never started recording — tear everything down with no file produced.
    /// <paramref name="notify"/> is false when the caller will surface its own error (session fault).</summary>
    public void CancelPreview(bool notify = true)
    {
        if (IsRecording || _stopping) return;
        _stopping = true;
        StopSnapshotTimer();
        try { _ = _session?.StopAsync(); } catch { }   // disposes the WGC capture; nothing to reassemble
        CloseWindows();
        _session = null;
        _previewing = false;
        _stopping = false;
        if (notify) PreviewDismissed?.Invoke(PreviewDismissedReason);
    }

    /// <summary>A throw escaped the capture callback (GPU device-loss/TDR, hybrid-GPU switch, display unplug, or
    /// ffmpeg failing to spawn). The session already stopped accepting frames; finalize whatever we have and
    /// surface the cause instead of the app crashing. Runs on the UI thread (marshalled by the caller).</summary>
    private void OnSessionError(string message)
    {
        _faultMessage = message;
        if (IsRecording) _ = StopAsync();       // finalize the partial recording; StopAsync reports via Finished/Failed
        else if (!_stopping)                    // preview fault: tear down with no file and surface the cause
        {
            CancelPreview(notify: false);
            Failed?.Invoke(message);
        }
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

    /// <summary>Returns false when there is no session to stop. Already-stopping returns true so the main
    /// window doesn't clear an in-progress save.</summary>
    public async Task<bool> StopAsync()
    {
        var session = _session;
        if (StreamEndGate.StopReportsFailure(session is not null)) return false;
        if (!StreamEndGate.StopHasWork(session is not null, _stopping)) return true;
        StopSnapshotTimer();
        _stopping = true;
        IsRecording = false;

        try
        {
            _monitor?.SetStatus("整理檔案中…");
            Status?.Invoke("整理檔案中…");
            // Name the file 側錄_<title>.mp4 (mac parity) — read the page title while the player is still alive,
            // before StopAsync/CloseWindows tears it down. Blank/unknown title → 側錄.mp4.
            var title = _player is null ? null : await _player.TitleAsync();
            _outputPath = OutputPaths.SideRecordOutput(_jobDir, title);
            var result = await session!.StopAsync();
            Status?.Invoke($"session: frames={result.VideoFrames} dropped={result.VideoFramesDropped} audioBytes={result.AudioBytes} ffmpegExit={result.FfmpegExitCode} {result.Error}");

            var (ok, err) = await SegmentReassembler.ReassembleAsync(
                _segmentsDir, _outputPath, _ffmpegPath, new ProcessRunner(), _audioPcmPath,
                keepFullVideo: _audioDeviceLost);

            CloseWindows();

            if (ok) Finished?.Invoke(_outputPath);
            else Failed?.Invoke(_faultMessage ?? err ?? result.Error ?? "錄製失敗");
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
        return true;
    }

    private void CloseWindows()
    {
        try { _monitor?.CloseMonitor(); } catch { }
        try { _player?.Close(); } catch { }
        _monitor = null;
        _player = null;
    }
}
