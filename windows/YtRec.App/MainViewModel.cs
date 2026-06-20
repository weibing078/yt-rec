using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using YtRec.Core;

namespace YtRec.App;

/// <summary>
/// Phase 1 main-window logic: drives the download track (Track A) via YtRec.Core.
/// Screen-capture / monitor (Track B) is Phase 2 and not wired here yet.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        _ui = DispatcherQueue.GetForCurrentThread();
        StartCommand = new RelayCommand(async () => await StartAsync(), () => !IsBusy);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        RefreshTools();
    }

    // ── Bound state ──────────────────────────────────────────────
    private string _urlText = "";
    public string UrlText { get => _urlText; set { if (Set(ref _urlText, value)) Raise(nameof(CanStart)); } }

    private string _sectionStart = "";
    public string SectionStart { get => _sectionStart; set => Set(ref _sectionStart, value); }

    private string _sectionEnd = "";
    public string SectionEnd { get => _sectionEnd; set => Set(ref _sectionEnd, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { Raise(nameof(CanStart)); StartCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); } }
    }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private string? _jobTitle;
    public string? JobTitle { get => _jobTitle; private set { Set(ref _jobTitle, value); Raise(nameof(HasJob)); } }
    public bool HasJob => !string.IsNullOrEmpty(JobTitle);

    private string? _warningText;
    public string? WarningText { get => _warningText; private set { Set(ref _warningText, value); Raise(nameof(HasWarning)); } }
    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    public bool CanStart => !IsBusy && YtUrl.VideoId(UrlText) != null;

    public ObservableCollection<RecentFile> RecentFiles { get; } = new();

    public RelayCommand StartCommand { get; }
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
