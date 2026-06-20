namespace YtRec.Core;

/// <summary>Track A download strategy (pure logic). Mirrors mac DownloadStrategy in YtDlpEngine.swift.</summary>
public enum DownloadStrategy
{
    FromStart, // --live-from-start
    Normal,    // plain download
    Degraded,  // drop to 720p h264 to survive
}

public static class DownloadStrategyExtensions
{
    /// <summary>Extra yt-dlp arguments this strategy appends.</summary>
    public static string[] Arguments(this DownloadStrategy strategy, string liveStatus) => strategy switch
    {
        DownloadStrategy.FromStart => liveStatus == "is_upcoming"
            ? new[] { "--live-from-start", "--wait-for-video", "60" }
            : new[] { "--live-from-start" },
        DownloadStrategy.Normal => Array.Empty<string>(),
        DownloadStrategy.Degraded => new[] { "-S", "res:720,vcodec:h264" },
        _ => Array.Empty<string>(),
    };
}
