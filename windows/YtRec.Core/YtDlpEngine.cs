namespace YtRec.Core;

/// <summary>Result of a download attempt. Mirrors mac YtDlpEngine.Outcome.</summary>
public abstract record DownloadOutcome
{
    public sealed record Success(string File) : DownloadOutcome;
    public sealed record TerminalFailure(string Reason) : DownloadOutcome;
    public sealed record Marathon : DownloadOutcome;          // very long live → side-record only
    public sealed record SkippedAutoLive : DownloadOutcome;   // auto mode, in-progress live → side-record only
    public sealed record Cancelled : DownloadOutcome;
}

/// <summary>Track A: yt-dlp smart-polling download engine. Probe → decide → multi-strategy → 30s poll
/// until success / terminal failure / cancel. Mirrors mac YtDlpEngine.swift.
/// The IProcessRunner is injected (a fresh one per attempt) so orchestration is unit-testable.</summary>
public sealed class YtDlpEngine
{
    private static readonly string[] StrategyLabels = { "方案A(原始串流)", "方案B(就緒檔)", "方案C(降畫質)" };

    private readonly string _ytdlp;
    private readonly string? _ffmpegDir;
    private readonly Func<IProcessRunner> _runnerFactory;

    public Action<string>? OnStatus;          // progress text (any thread)
    public Action<ProbeInfo>? OnProbe;

    public YtDlpEngine(string ytdlpPath, string? ffmpegDir, Func<IProcessRunner> runnerFactory)
    {
        _ytdlp = ytdlpPath;
        _ffmpegDir = ffmpegDir;
        _runnerFactory = runnerFactory;
    }

    /// <param name="autoMode">true = only download ended VODs; in-progress live → SkippedAutoLive.</param>
    /// <param name="section">non-null = "grab one section" (single download, no polling, no live strategies).</param>
    /// <param name="pollSeconds">delay between failed rounds (injectable for tests).</param>
    public async Task<DownloadOutcome> StartAsync(string url, string outputDir, int maxHeight,
        bool autoMode = false, string? section = null, int pollSeconds = 30, CancellationToken ct = default)
    {
        var baseArgs = new List<string>
        {
            "--newline", "--no-colors", "--no-playlist", "--ignore-config",
            "--retries", "10", "--fragment-retries", "60",
            "-N", "4",
            "--merge-output-format", "mp4",
            "-o", Path.Combine(outputDir, "%(title).70B [%(id)s].%(ext)s"),
        };
        if (_ffmpegDir != null) { baseArgs.Add("--ffmpeg-location"); baseArgs.Add(_ffmpegDir); }
        var sort = maxHeight > 0 ? $"res:{maxHeight},vcodec:h264,acodec:m4a" : "res,vcodec:h264,acodec:m4a";

        // ── Probe (id/title/live_status + start time; also validates the URL) ──
        var liveStatus = "NA";
        double? releaseTs = null;
        OnStatus?.Invoke("正在分析直播狀態…");
        var probeArgs = new[]
        {
            "--no-warnings", "--no-playlist", "--skip-download", "--ignore-config",
            "--print", "%(id)s\t%(title)s\t%(live_status)s\t%(release_timestamp)s", url,
        };
        var probe = await _runnerFactory().RunAsync(_ytdlp, probeArgs, ct: ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return new DownloadOutcome.Cancelled();

        if (probe.ExitCode == 0)
        {
            var info = probe.Output.Split('\n')
                .Select(YtDlpParse.ParseProbe)
                .LastOrDefault(p => p is not null);
            if (info is not null)
            {
                liveStatus = info.LiveStatus;
                releaseTs = info.ReleaseTimestamp;
                OnProbe?.Invoke(info);
            }
        }
        else if (YtDlpParse.IsTerminalFailure(probe.Output))
        {
            return new DownloadOutcome.TerminalFailure(YtDlpParse.FriendlyFailure(probe.Output));
        }

        // ── Post-probe orchestration decision (pure function gate) ──
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (YtDlpParse.DecideOutcome(liveStatus, releaseTs, now, autoMode, section != null))
        {
            case ProbeOutcome.Marathon:
                return new DownloadOutcome.Marathon();
            case ProbeOutcome.Section:
                return await DownloadSectionAsync(baseArgs, sort, section!, url, outputDir, ct).ConfigureAwait(false);
            case ProbeOutcome.SkippedAutoLive:
                return new DownloadOutcome.SkippedAutoLive();
            case ProbeOutcome.Proceed:
                break;
        }

        // ── Multi-strategy attempts with smart polling ──
        var order = YtDlpParse.StrategyOrder(liveStatus);
        var round = 0;
        while (!ct.IsCancellationRequested)
        {
            round++;
            for (var i = 0; i < order.Length; i++)
            {
                if (ct.IsCancellationRequested) return new DownloadOutcome.Cancelled();
                var strat = order[i];
                var label = StrategyLabels[Math.Min(i, 2)];
                OnStatus?.Invoke($"第 {round} 輪・{label} 嘗試中…");

                var startTime = DateTime.UtcNow;
                var extra = strat.Arguments(liveStatus);
                // degraded strategy supplies its own sort; others use the shared -S sort
                var args = strat == DownloadStrategy.Degraded
                    ? Concat(baseArgs, extra, new[] { url })
                    : Concat(baseArgs, new[] { "-S", sort }, extra, new[] { url });

                var r = await _runnerFactory().RunAsync(_ytdlp, args, onLine: line =>
                {
                    var t = YtDlpParse.ProgressText(line);
                    if (t != null) OnStatus?.Invoke($"{label}・{t}");
                }, ct: ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested || r.WasCancelled) return new DownloadOutcome.Cancelled();

                if (r.ExitCode == 0)
                {
                    var file = NewestVideoFile(outputDir, startTime.AddSeconds(-5));
                    if (file != null) return new DownloadOutcome.Success(file);
                    // exit 0 but no output file → treat as retryable
                }
                if (YtDlpParse.IsTerminalFailure(r.Output))
                    return new DownloadOutcome.TerminalFailure(YtDlpParse.FriendlyFailure(r.Output));
            }

            // whole round failed → smart poll
            OnStatus?.Invoke($"素材尚未就緒，{pollSeconds} 秒後重試（已輪詢 {round} 輪）");
            for (var s = 0; s < pollSeconds; s++)
            {
                if (ct.IsCancellationRequested) return new DownloadOutcome.Cancelled();
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return new DownloadOutcome.Cancelled(); }
            }
        }
        return new DownloadOutcome.Cancelled();
    }

    /// <summary>"Grab one section" via --download-sections (single shot, keyframe-aligned, no re-encode, no poll).</summary>
    private async Task<DownloadOutcome> DownloadSectionAsync(List<string> baseArgs, string sort,
        string section, string url, string outputDir, CancellationToken ct)
    {
        OnStatus?.Invoke("下載片段中…");
        var startTime = DateTime.UtcNow;
        var args = Concat(baseArgs, new[] { "-S", sort, "--download-sections", section, url });
        var r = await _runnerFactory().RunAsync(_ytdlp, args, onLine: line =>
        {
            var t = YtDlpParse.ProgressText(line);
            if (t != null) OnStatus?.Invoke($"片段・{t}");
        }, ct: ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested || r.WasCancelled) return new DownloadOutcome.Cancelled();
        if (r.ExitCode == 0)
        {
            var file = NewestVideoFile(outputDir, startTime.AddSeconds(-5));
            if (file != null) return new DownloadOutcome.Success(file);
        }
        if (YtDlpParse.IsTerminalFailure(r.Output))
            return new DownloadOutcome.TerminalFailure(YtDlpParse.FriendlyFailure(r.Output));
        var tail = r.Output.Length <= 160 ? r.Output : r.Output[^160..];
        return new DownloadOutcome.TerminalFailure("片段下載失敗：" + tail);
    }

    private static readonly string[] VideoExts = { ".mp4", ".mkv", ".webm", ".mov", ".m4a" };

    /// <summary>Newest finished video in dir (excludes .part temps, requires &gt;100KB and newer than the cutoff).</summary>
    public static string? NewestVideoFile(string dir, DateTime newerThanUtc)
    {
        if (!Directory.Exists(dir)) return null;
        FileInfo? best = null;
        foreach (var path in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(path);
            if (name.Contains(".part")) continue;
            if (!VideoExts.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            var fi = new FileInfo(path);
            if (fi.LastWriteTimeUtc <= newerThanUtc || fi.Length <= 100_000) continue;
            if (best is null || fi.LastWriteTimeUtc > best.LastWriteTimeUtc) best = fi;
        }
        return best?.FullName;
    }

    private static string[] Concat(params IEnumerable<string>[] parts)
    {
        var list = new List<string>();
        foreach (var p in parts) list.AddRange(p);
        return list.ToArray();
    }
}
