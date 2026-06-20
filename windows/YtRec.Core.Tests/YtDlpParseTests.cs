using YtRec.Core;

namespace YtRec.Core.Tests;

// Ported from mac PureLogicTests.swift (YtDlpParseTests) + added strategy/decision coverage.
public class YtDlpParseTests
{
    [Fact]
    public void TerminalFailure()
    {
        Assert.True(YtDlpParse.IsTerminalFailure("ERROR: [youtube] xxx: Private video. Sign in if..."));
        Assert.True(YtDlpParse.IsTerminalFailure("ERROR: Video unavailable"));
        Assert.True(YtDlpParse.IsTerminalFailure("This video is available to this channel's members-only"));
        Assert.False(YtDlpParse.IsTerminalFailure("ERROR: HTTP Error 404: Not Found"));
        Assert.False(YtDlpParse.IsTerminalFailure("This live event has ended"));
    }

    [Fact]
    public void TerminalFailureMorePhrases()
    {
        Assert.True(YtDlpParse.IsTerminalFailure("This video is age-restricted"));
        Assert.True(YtDlpParse.IsTerminalFailure("The account associated with this video has been terminated"));
        Assert.True(YtDlpParse.IsTerminalFailure("The uploader has not made this video available in your country who has blocked it in your country"));
        Assert.False(YtDlpParse.IsTerminalFailure("ERROR: unable to download video data: HTTP Error 503"));
        Assert.False(YtDlpParse.IsTerminalFailure("WARNING: no video formats found"));
    }

    [Fact]
    public void Progress()
    {
        var t = YtDlpParse.ProgressText("[download]  45.2% of ~  1.20GiB at    3.40MiB/s ETA 00:12");
        Assert.NotNull(t);
        Assert.Contains("45.2%", t);
        Assert.Contains("3.40MiB/s", t);
        Assert.Null(YtDlpParse.ProgressText("[youtube] Extracting URL"));
    }

    [Fact]
    public void ProgressWithFragment()
    {
        var t = YtDlpParse.ProgressText("[download]  50.0% of ~ 10.00MiB at  1.00MiB/s (frag 42)");
        Assert.NotNull(t);
        Assert.Contains("50.0%", t);
        Assert.Contains("第 42 段", t);
        Assert.Contains("1.00MiB/s", t);
    }

    [Fact]
    public void ProgressTextPlainBytesPerSec()
    {
        var t = YtDlpParse.ProgressText("[download]   5.0% of ~1.00MiB at 0.50B/s");
        Assert.NotNull(t);
        Assert.Contains("下載 5.0%", t);
        Assert.Contains("0.50B/s", t);
    }

    [Fact]
    public void ProgressTextNonDownloadLineNil() =>
        Assert.Null(YtDlpParse.ProgressText("[info] something else"));

    [Fact]
    public void RoundExtraction()
    {
        Assert.Equal("7", YtDlpParse.FirstMatch("素材尚未就緒，30 秒後重試（已輪詢 7 輪）", @"已輪詢 (\d+) 輪"));
        Assert.Null(YtDlpParse.FirstMatch("沒有輪數", @"已輪詢 (\d+) 輪"));
    }

    [Fact]
    public void ProbeParse()
    {
        var info = YtDlpParse.ParseProbe("dQw4w9WgXcQ\t標題 有 空格\tis_live");
        Assert.Equal("dQw4w9WgXcQ", info?.Id);
        Assert.Equal("標題 有 空格", info?.Title);
        Assert.Equal("is_live", info?.LiveStatus);
        Assert.Null(info?.ReleaseTimestamp);
        Assert.Null(YtDlpParse.ParseProbe("not a probe line"));
    }

    [Fact]
    public void ProbeParseTimestamp()
    {
        var info = YtDlpParse.ParseProbe("dQw4w9WgXcQ\t標題\tis_live\t1700000000");
        Assert.Equal(1_700_000_000, info?.ReleaseTimestamp);
        Assert.Null(YtDlpParse.ParseProbe("dQw4w9WgXcQ\t標題\tis_live\tNA")?.ReleaseTimestamp);
    }

    [Fact]
    public void ProbeParseRejectsMalformed()
    {
        Assert.Null(YtDlpParse.ParseProbe("short\ttitle\tis_live"));   // id not 11 chars
        Assert.Null(YtDlpParse.ParseProbe("abcDEF12345\tonly_two"));   // <3 fields
        Assert.Null(YtDlpParse.ParseProbe(""));
    }

    [Fact]
    public void ProbeParseTitleWithTab()
    {
        var info = YtDlpParse.ParseProbe("dQw4w9WgXcQ\t標題\t含tab\tis_live\t1700000000");
        Assert.Equal("dQw4w9WgXcQ", info?.Id);
        Assert.Equal("is_live", info?.LiveStatus);          // not "含tab"
        Assert.Equal(1_700_000_000, info?.ReleaseTimestamp);
        Assert.Equal("標題\t含tab", info?.Title);            // middle fields are all title
    }

    [Fact]
    public void Marathon()
    {
        const double now = 1_000_000_000.0;
        Assert.True(YtDlpParse.IsMarathon("is_live", now - 5 * 3600, now));
        Assert.False(YtDlpParse.IsMarathon("is_live", now - 2 * 3600, now));
        Assert.False(YtDlpParse.IsMarathon("post_live", now - 10 * 3600, now));
        Assert.False(YtDlpParse.IsMarathon("is_live", null, now));
    }

    [Fact]
    public void MarathonBoundary()
    {
        const double now = 1_000_000_000.0;
        Assert.False(YtDlpParse.IsMarathon("is_live", now - 4 * 3600, now));          // exactly 4h → no
        Assert.True(YtDlpParse.IsMarathon("is_live", now - (4 * 3600 + 1), now));     // 4h+1s → yes
        Assert.False(YtDlpParse.IsMarathon("is_live", now - (3 * 3600 + 59 * 60), now)); // 3h59m → no
    }

    [Fact]
    public void StrategyOrderByLiveStatus()
    {
        Assert.Equal(new[] { DownloadStrategy.FromStart, DownloadStrategy.Normal, DownloadStrategy.Degraded },
            YtDlpParse.StrategyOrder("is_live"));
        Assert.Equal(new[] { DownloadStrategy.FromStart, DownloadStrategy.Normal, DownloadStrategy.Degraded },
            YtDlpParse.StrategyOrder("NA"));
        Assert.Equal(new[] { DownloadStrategy.Normal, DownloadStrategy.FromStart, DownloadStrategy.Degraded },
            YtDlpParse.StrategyOrder("post_live"));
    }

    [Theory]
    [InlineData("post_live", true)]
    [InlineData("was_live", true)]
    [InlineData("not_live", true)]
    [InlineData("is_live", false)]
    [InlineData("is_upcoming", false)]
    [InlineData("NA", false)]
    public void AutoShouldDownload(string status, bool expected) =>
        Assert.Equal(expected, YtDlpParse.AutoShouldDownload(status));

    [Fact]
    public void DecideOutcomeOrdering()
    {
        const double now = 1_000_000_000.0;
        // marathon beats everything
        Assert.Equal(ProbeOutcome.Marathon,
            YtDlpParse.DecideOutcome("is_live", now - 5 * 3600, now, autoMode: true, hasSection: true));
        // section beats auto-skip / proceed
        Assert.Equal(ProbeOutcome.Section,
            YtDlpParse.DecideOutcome("is_live", null, now, autoMode: true, hasSection: true));
        // auto mode + in-progress live → skip
        Assert.Equal(ProbeOutcome.SkippedAutoLive,
            YtDlpParse.DecideOutcome("is_live", null, now, autoMode: true, hasSection: false));
        // auto mode + ended VOD → proceed
        Assert.Equal(ProbeOutcome.Proceed,
            YtDlpParse.DecideOutcome("post_live", null, now, autoMode: true, hasSection: false));
        // non-auto, non-marathon → proceed
        Assert.Equal(ProbeOutcome.Proceed,
            YtDlpParse.DecideOutcome("is_live", now - 3600, now, autoMode: false, hasSection: false));
    }

    [Fact]
    public void StrategyArguments()
    {
        Assert.Equal(new[] { "--live-from-start" }, DownloadStrategy.FromStart.Arguments("is_live"));
        Assert.Equal(new[] { "--live-from-start", "--wait-for-video", "60" },
            DownloadStrategy.FromStart.Arguments("is_upcoming"));
        Assert.Empty(DownloadStrategy.Normal.Arguments("is_live"));
        Assert.Equal(new[] { "-S", "res:720,vcodec:h264" }, DownloadStrategy.Degraded.Arguments("is_live"));
    }

    [Fact]
    public void FriendlyFailureMessages()
    {
        Assert.Equal("影片已被設為私人／隱藏", YtDlpParse.FriendlyFailure("ERROR: Private video"));
        Assert.Equal("年齡限制內容，YouTube 要求登入確認年齡，此來源無法下載",
            YtDlpParse.FriendlyFailure("Sign in to confirm your age"));
        Assert.StartsWith("無法下載：", YtDlpParse.FriendlyFailure("some unknown error"));
    }
}
