using Microsoft.UI.Dispatching;
using YtRec.Capture;
using YtRec.Core;

namespace YtRec.App;

/// <summary>Drives one screen side-recording end to end on the UI thread: spin up the off-screen player,
/// wait out the audio-decode settle window, acquire loopback audio (Win11 per-process / Win10 system),
/// run the <see cref="RecordingSession"/>, show the floating monitor, then finalize the segments into a
/// clean MP4. The app's ViewModel listens to the events.</summary>
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
    private bool _stopping;

    public bool IsRecording { get; private set; }

    public CaptureController(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public async Task StartAsync(string url, string jobDir, string title, int fps = 30)
    {
        var watchUrl = PlayerAssets.WatchUrlFrom(url) ?? throw new InvalidOperationException("不是有效的 YouTube 影片網址");
        _segmentsDir = SegmentReassembler.SegmentsDir(jobDir);
        _audioPcmPath = SegmentReassembler.AudioPath(jobDir);
        _outputPath = OutputPaths.SideRecordOutput(jobDir);
        Directory.CreateDirectory(_segmentsDir);

        Status?.Invoke("開啟播放器…");
        _player = new Win32PlayerHost();
        await _player.LoadAsync(watchUrl, Path.Combine(jobDir, ".work", "webview2"));

        // Audio-decode settle window (mac §8: ~4 s after the player starts). The session-gate then anchors
        // the writer to the first audio sample, so any frames captured during this wait are dropped cleanly.
        Status?.Invoke("等待串流開始…");
        await Task.Delay(4000);

        var audio = MakeAudio(_player.BrowserProcessId);

        _monitor = new MonitorWindow(title);
        _monitor.StopRequested += () => _ = StopAsync();
        _monitor.Activate();

        // Capture the player's monitor and crop to its rectangle (WGC window-capture can't see the WebView2
        // video swapchain). The player is topmost/visible at a known rect.
        var hmon = WindowFinder.MonitorForWindow(_player.Hwnd);
        var crop = WindowFinder.CropRect(_player.Hwnd, hmon);
        Status?.Invoke($"capture crop=({crop.X},{crop.Y}) {crop.W}x{crop.H}");
        _session = new RecordingSession(hmon, crop, audio, _ffmpegPath, _segmentsDir, _audioPcmPath, fps)
        {
            OnPreviewFrame = (buf, w, h) => _monitor?.UpdatePreview(buf, w, h),
        };
        _player.Ended += () => _ui.TryEnqueue(() => _ = StopAsync());

        _session.Start();
        IsRecording = true;
        _monitor.SetStatus(AudioCapability.IsolatedAudioSupported(OsBuild)
            ? "錄製中（只錄這個串流的聲音）"
            : "錄製中（此電腦會錄到全系統聲音）");
        Status?.Invoke("錄製中");
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
