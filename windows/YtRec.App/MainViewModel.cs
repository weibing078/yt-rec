using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using YtRec.Core;

namespace YtRec.App;

/// <summary>
/// Main-window logic: drives the download track (Track A) via YtRec.Core and the screen side-record track
/// (Track B) via YtRec.Capture / <see cref="CaptureController"/>.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _cts;
    private CaptureController? _capture;
    private DispatcherQueueTimer? _guardTimer;
    private DateTime _recordStart;

    public MainViewModel()
    {
        _ui = DispatcherQueue.GetForCurrentThread();
        StartCommand = new RelayCommand(async () => await StartAsync(), () => CanStart);
        RecordCommand = new RelayCommand(async () => await RecordAsync(), () => CanRecord);
        StopCommand = new RelayCommand(StopOrCancel, () => IsBusy || IsRecording);
        RefreshTools();
        DetectAudioCapability();
    }

    // ── Bound state ──────────────────────────────────────────────
    private string _urlText = "";
    public string UrlText
    {
        get => _urlText;
        set { if (Set(ref _urlText, value)) { Raise(nameof(CanStart)); Raise(nameof(CanRecord)); StartCommand.RaiseCanExecuteChanged(); RecordCommand.RaiseCanExecuteChanged(); } }
    }

    private string _sectionStart = "";
    public string SectionStart { get => _sectionStart; set => Set(ref _sectionStart, value); }

    private string _sectionEnd = "";
    public string SectionEnd { get => _sectionEnd; set => Set(ref _sectionEnd, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { Raise(nameof(CanStart)); Raise(nameof(CanRecord)); Raise(nameof(IsBusyUi)); StartCommand.RaiseCanExecuteChanged(); RecordCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); } }
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        private set { if (Set(ref _isRecording, value)) { Raise(nameof(CanStart)); Raise(nameof(CanRecord)); Raise(nameof(IsBusyUi)); StartCommand.RaiseCanExecuteChanged(); RecordCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); } }
    }

    /// <summary>Either track is working — drives the progress ring.</summary>
    public bool IsBusyUi => IsBusy || IsRecording;

    /// <summary>Recording duration cap (auto-finalize at the cap). Default 6 h (mac §8).</summary>
    public DurationCap DurationCap { get; set; } = DurationCap.SixHours;

    private string? _audioNotice;
    public string? AudioNotice
    {
        get => _audioNotice;
        private set { Set(ref _audioNotice, value); Raise(nameof(HasAudioNotice)); }
    }
    public bool HasAudioNotice => !string.IsNullOrEmpty(AudioNotice);

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private string? _jobTitle;
    public string? JobTitle { get => _jobTitle; private set { Set(ref _jobTitle, value); Raise(nameof(HasJob)); } }
    public bool HasJob => !string.IsNullOrEmpty(JobTitle);

    private string? _warningText;
    public string? WarningText { get => _warningText; private set { Set(ref _warningText, value); Raise(nameof(HasWarning)); } }
    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    public bool CanStart => !IsBusy && !IsRecording && YtUrl.VideoId(UrlText) != null;
    public bool CanRecord => !IsBusy && !IsRecording && YtUrl.VideoId(UrlText) != null;

    public ObservableCollection<RecentFile> RecentFiles { get; } = new();

    public RelayCommand StartCommand { get; }
    public RelayCommand RecordCommand { get; }
    public RelayCommand StopCommand { get; }

    // ── Actions ──────────────────────────────────────────────────
    public void RefreshTools()
    {
        var missing = BinaryLocator.MissingTools();
        WarningText = missing.Count == 0
            ? null
            : $"缺少工具：{string.Join(", ", missing)} — 請執行 tools\\setup-binaries.ps1";
    }

    private async Task StartAsync()
    {
        if (!CanStart) return;
        var ytdlp = BinaryLocator.Resolve(BinaryLocator.Tool.YtDlp);
        if (ytdlp == null) { RefreshTools(); return; }
        var ffmpegDir = Path.GetDirectoryName(BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg) ?? "");

        var outputDir = OutputPaths.NewTaskFolder(UrlText);
        Directory.CreateDirectory(outputDir);

        string? section = null;
        var sec = Timecode.Section(SectionStart, SectionEnd);
        if (sec is { } s) section = s.Arg;

        IsBusy = true;
        StatusText = "正在分析…";
        _cts = new CancellationTokenSource();

        var engine = new YtDlpEngine(ytdlp, string.IsNullOrEmpty(ffmpegDir) ? null : ffmpegDir,
            () => new ProcessRunner());
        engine.OnStatus = t => _ui.TryEnqueue(() => StatusText = t);
        engine.OnProbe = p => _ui.TryEnqueue(() => JobTitle = p.Title);

        var outcome = await engine.StartAsync(UrlText, outputDir, maxHeight: 1080, section: section, ct: _cts.Token);

        switch (outcome)
        {
            case DownloadOutcome.Success ok:
                StatusText = "完成";
                AddRecent(ok.File, section != null ? FileKind.Clip : FileKind.Native);
                break;
            case DownloadOutcome.TerminalFailure f:
                StatusText = f.Reason;
                break;
            case DownloadOutcome.Marathon:
                StatusText = "馬拉松直播：此版本下載軌不處理（待 Phase 2 螢幕側錄）";
                break;
            case DownloadOutcome.SkippedAutoLive:
                StatusText = "進行中直播：待 Phase 2 螢幕側錄";
                break;
            case DownloadOutcome.Cancelled:
                StatusText = "已取消";
                break;
        }
        IsBusy = false;
        _cts = null;
    }

    // ── Track B: screen side-record ──────────────────────────────
    private void DetectAudioCapability()
    {
        if (!AudioCapability.IsolatedAudioSupported(Environment.OSVersion.Version.Build))
            AudioNotice = "此電腦是 Windows 10：側錄會錄到全系統聲音，無法只隔離這個串流。" +
                          "（只錄此串流的聲音需要 Windows 11）";
    }

    private async Task RecordAsync()
    {
        if (!CanRecord) return;
        var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg);
        if (ffmpeg == null) { RefreshTools(); return; }

        var jobDir = OutputPaths.NewTaskFolder(UrlText);
        Directory.CreateDirectory(jobDir);

        _capture = new CaptureController(ffmpeg);
        _capture.Status += t => _ui.TryEnqueue(() => StatusText = t);
        _capture.Finished += path => _ui.TryEnqueue(() => OnRecordFinished(path));
        _capture.Failed += msg => _ui.TryEnqueue(() => OnRecordFailed(msg));

        JobTitle = "螢幕側錄";
        StatusText = "準備中…";
        IsRecording = true;
        _recordStart = DateTime.Now;
        StartGuardTimer();

        try { await _capture.StartAsync(UrlText, jobDir, "螢幕側錄"); }
        catch (Exception e) { OnRecordFailed(e.Message); }
    }

    private void StopOrCancel()
    {
        if (IsRecording) _ = _capture?.StopAsync();
        else _cts?.Cancel();
    }

    private void OnRecordFinished(string path)
    {
        StopGuardTimer();
        StatusText = "完成";
        if (File.Exists(path)) AddRecent(path, FileKind.Sidecar);
        ResetRecordState();
    }

    private void OnRecordFailed(string message)
    {
        StopGuardTimer();
        StatusText = "側錄失敗：" + message;
        ResetRecordState();
    }

    private void ResetRecordState()
    {
        IsRecording = false;
        JobTitle = null;
        _capture = null;
    }

    // Duration cap + disk guard, polled every 10 s while recording (mac §8).
    private void StartGuardTimer()
    {
        _guardTimer = _ui.CreateTimer();
        _guardTimer.Interval = TimeSpan.FromSeconds(10);
        _guardTimer.Tick += (_, _) =>
        {
            if (!IsRecording) { StopGuardTimer(); return; }
            var elapsed = (long)(DateTime.Now - _recordStart).TotalSeconds;
            if (DurationCap.ShouldAutoFinalize(elapsed))
            {
                StatusText = "已達錄製時間上限，自動存檔";
                _ = _capture?.StopAsync();
                return;
            }
            var free = DiskGuard.FreeBytes(() => TryFreeBytes(OutputPaths.Root));
            if (DiskGuard.ShouldStopRecording(free))
            {
                StatusText = "磁碟空間不足，已自動存檔";
                _ = _capture?.StopAsync();
            }
        };
        _guardTimer.Start();
    }

    private void StopGuardTimer()
    {
        _guardTimer?.Stop();
        _guardTimer = null;
    }

    private static long? TryFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }

    /// <summary>Disaster recovery on launch: reassemble any side-record that was interrupted (kill-9 / crash).
    /// Idempotent; skips the in-use job (none at launch). Runs ffmpeg off the UI thread (mac §7).</summary>
    public async Task RecoverOrphansAsync()
    {
        var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg);
        if (ffmpeg == null || !Directory.Exists(OutputPaths.Root)) return;

        var results = await SegmentReassembler.RecoverAllAsync(
            OutputPaths.Root, activeJobDir: null, ffmpeg, new ProcessRunner(), OutputPaths.SideRecordOutput);

        var recovered = 0;
        foreach (var (dir, ok, _) in results)
            if (ok) { AddRecent(OutputPaths.SideRecordOutput(dir), FileKind.Sidecar); recovered++; }
        if (recovered > 0) StatusText = $"已修復 {recovered} 個中斷的側錄";
    }

    private void AddRecent(string path, FileKind kind)
    {
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { /* best effort */ }
        RecentFiles.Insert(0, new RecentFile(Path.GetFileName(path), path, kind, FormatBytes(size)));
        while (RecentFiles.Count > 5) RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    private static string FormatBytes(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; var i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return $"{v:0.#} {u[i]}";
    }

    // ── INotifyPropertyChanged ───────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FileKind { Native, Sidecar, Clip }

public sealed record RecentFile(string FileName, string FullPath, FileKind Kind, string SizeText)
{
    public string KindLabel => Kind switch
    {
        FileKind.Native => "下載原生檔",
        FileKind.Sidecar => "螢幕側錄",
        FileKind.Clip => "片段",
        _ => "",
    };
}

/// <summary>Output folder layout (mirrors mac ~/Movies/YT-Rec/&lt;timestamp title&gt;/).</summary>
public static class OutputPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "YT-Rec");

    public static string NewTaskFolder(string url)
    {
        var id = YtUrl.VideoId(url) ?? "video";
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return Path.Combine(Root, $"{stamp} {id}");
    }

    /// <summary>The finalized side-record file for a job dir. Used by both the live finalize and the
    /// launch-time disaster recovery so they target the same path.</summary>
    public static string SideRecordOutput(string jobDir) =>
        Path.Combine(jobDir, Path.GetFileName(jobDir.TrimEnd(Path.DirectorySeparatorChar)) + " 側錄.mp4");
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task>? _async;
    private readonly Action? _sync;
    private readonly Func<bool>? _can;
    private bool _running;

    public RelayCommand(Action execute, Func<bool>? canExecute = null) { _sync = execute; _can = canExecute; }
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null) { _async = execute; _can = canExecute; }

    public bool CanExecute(object? parameter) => !_running && (_can?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(null)) return;
        if (_async != null)
        {
            _running = true; RaiseCanExecuteChanged();
            try { await _async(); }
            finally { _running = false; RaiseCanExecuteChanged(); }
        }
        else _sync?.Invoke();
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
