using System.Globalization;
using System.Text.RegularExpressions;

namespace YtRec.Core;

/// <summary>Probe result from yt-dlp. releaseTimestamp is epoch seconds, null if unknown ("NA").</summary>
public sealed record ProbeInfo(string Id, string Title, string LiveStatus, double? ReleaseTimestamp);

/// <summary>Outcome of the post-probe orchestration decision.</summary>
public enum ProbeOutcome { Marathon, Section, SkippedAutoLive, Proceed }

/// <summary>yt-dlp output parsing + Track A decision logic (pure). Mirrors mac YtDlpParse in YtDlpEngine.swift.</summary>
public static class YtDlpParse
{
    /// <summary>Strategy order by live_status: in-progress prefers "from start"; ended prefers "normal".</summary>
    public static DownloadStrategy[] StrategyOrder(string liveStatus) => liveStatus switch
    {
        "post_live" or "was_live" or "not_live" =>
            new[] { DownloadStrategy.Normal, DownloadStrategy.FromStart, DownloadStrategy.Degraded },
        _ => // is_live / is_upcoming / NA / unknown → live-first
            new[] { DownloadStrategy.FromStart, DownloadStrategy.Normal, DownloadStrategy.Degraded },
    };

    /// <summary>Unrecoverable failures (hidden/deleted/restricted) → stop polling.</summary>
    public static bool IsTerminalFailure(string output)
    {
        var lower = output.ToLowerInvariant();
        string[] patterns =
        {
            "private video", "this video is private",
            "video unavailable", "removed by the uploader",
            "account associated with this video has been terminated",
            "members-only", "join this channel",
            "sign in to confirm your age", "age-restricted",
            "who has blocked it in your country",
            "sign in to confirm you",
        };
        return patterns.Any(lower.Contains);
    }

    /// <summary>Marathon: still live and started over the threshold → side-record only, no download track.</summary>
    public static bool IsMarathon(string liveStatus, double? releaseTimestamp, double now, double thresholdHours = 4)
    {
        if (liveStatus != "is_live" || releaseTimestamp is not { } start) return false;
        return now - start > thresholdHours * 3600;
    }

    /// <summary>Auto mode: only download finite ended VODs; in-progress/unknown → side-record only.</summary>
    public static bool AutoShouldDownload(string liveStatus) => liveStatus switch
    {
        "post_live" or "was_live" or "not_live" => true,
        _ => false,
    };

    /// <summary>Human progress text from a yt-dlp [download] line, e.g. "下載 45.2%・3.4MiB/s".</summary>
    public static string? ProgressText(string line)
    {
        if (!line.StartsWith("[download]", StringComparison.Ordinal)) return null;
        var pct = FirstMatch(line, @"(\d{1,3}(?:\.\d+)?)%");
        // [KMGT] optional: very slow downloads print bare "B/s" (e.g. 0.50B/s).
        var speed = FirstMatch(line, @"at\s+([0-9.]+\s*[KMGT]?i?B/s)");
        var frag = FirstMatch(line, @"\(frag (\d+)");
        var parts = new List<string>();
        if (pct != null) parts.Add($"下載 {pct}%");
        if (frag != null) parts.Add($"第 {frag} 段");
        if (speed != null) parts.Add(speed);
        return parts.Count == 0 ? null : string.Join("・", parts);
    }

    /// <summary>First capture group of the pattern in s, or null.</summary>
    public static string? FirstMatch(string s, string pattern)
    {
        var m = Regex.Match(s, pattern);
        return m.Success && m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : null;
    }

    /// <summary>Parse a tab-separated probe line. Tolerates tabs inside the title by reading the last two fields.</summary>
    public static ProbeInfo? ParseProbe(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 4 || parts[0].Length != 11)
        {
            if (parts.Length == 3 && parts[0].Length == 11)
                return new ProbeInfo(parts[0], parts[1], parts[2], null);
            return null;
        }
        // Last two fields are always live_status + release_timestamp; the middle is the (tab-containing) title.
        var ts = double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : (double?)null;
        var liveStatus = parts[^2];
        var title = string.Join('\t', parts[1..^2]);
        return new ProbeInfo(parts[0], title, liveStatus, ts);
    }

    /// <summary>Post-probe orchestration (order = marathon → section → auto-skip → proceed).</summary>
    public static ProbeOutcome DecideOutcome(string liveStatus, double? releaseTimestamp, double now,
        bool autoMode, bool hasSection)
    {
        if (IsMarathon(liveStatus, releaseTimestamp, now)) return ProbeOutcome.Marathon;
        if (hasSection) return ProbeOutcome.Section;
        if (autoMode && !AutoShouldDownload(liveStatus)) return ProbeOutcome.SkippedAutoLive;
        return ProbeOutcome.Proceed;
    }

    /// <summary>Localized, user-facing reason for a terminal failure.</summary>
    public static string FriendlyFailure(string output)
    {
        var lower = output.ToLowerInvariant();
        if (lower.Contains("private video") || lower.Contains("this video is private"))
            return "影片已被設為私人／隱藏";
        if (lower.Contains("members-only") || lower.Contains("join this channel"))
            return "會員限定內容（v1 僅支援公開直播）";
        // Age restriction must precede the generic "sign in to confirm you" check.
        if (lower.Contains("age-restricted") || lower.Contains("sign in to confirm your age"))
            return "年齡限制內容，YouTube 要求登入確認年齡，此來源無法下載";
        if (lower.Contains("account associated with this video has been terminated"))
            return "上傳此影片的帳號已被終止";
        if (lower.Contains("blocked it in your country"))
            return "此影片在你所在地區被封鎖";
        if (lower.Contains("sign in to confirm you"))
            return "YouTube 要求登入驗證，此來源無法下載";
        if (lower.Contains("video unavailable") || lower.Contains("removed"))
            return "影片已下架／刪除";
        var tail = output.Length <= 160 ? output : output[^160..];
        return "無法下載：" + tail;
    }
}
