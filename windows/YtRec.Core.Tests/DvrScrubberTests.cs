using System.Linq;
using YtRec.Core;

namespace YtRec.Core.Tests;

public class DvrScrubberTests
{
    [Theory]
    [InlineData(60, false)]   // ≤ 90 → no scrubber
    [InlineData(90, false)]   // boundary: exactly 90 → still no
    [InlineData(91, true)]    // > 90 → yes
    [InlineData(3600, true)]
    public void CanScrubOnlyPastNinetySeconds(double window, bool expected)
        => Assert.Equal(expected, DvrScrubber.CanScrub(window));

    [Fact]
    public void LiveFracIsOneAtEdgeZeroAtOldestHalfwayInBetween()
    {
        Assert.Equal(1.0, DvrScrubber.LiveFrac(600, 0), 3);
        Assert.Equal(0.0, DvrScrubber.LiveFrac(600, 600), 3);
        Assert.Equal(0.5, DvrScrubber.LiveFrac(600, 300), 3);
    }

    [Fact]
    public void LiveFracClampsOutOfRange()
    {
        Assert.Equal(1.0, DvrScrubber.LiveFrac(600, -10), 3);   // ahead of live
        Assert.Equal(0.0, DvrScrubber.LiveFrac(600, 9999), 3);  // beyond oldest
    }

    [Fact]
    public void ShownFracPrefersDragThenSettleThenLive()
    {
        Assert.Equal(0.25, DvrScrubber.ShownFrac(600, 0, dragFrac: 0.25, settleTargetBehindSec: 120), 3); // drag wins
        Assert.Equal(0.8, DvrScrubber.ShownFrac(600, 0, dragFrac: null, settleTargetBehindSec: 120), 3);  // settle target
        Assert.Equal(DvrScrubber.LiveFrac(600, 60), DvrScrubber.ShownFrac(600, 60, null, null), 6);       // live
    }

    [Fact]
    public void BehindForFracInvertsFraction()
    {
        Assert.Equal(0, DvrScrubber.BehindForFrac(600, 1.0), 3);    // live edge → 0 behind
        Assert.Equal(600, DvrScrubber.BehindForFrac(600, 0.0), 3);  // oldest → full window behind
        Assert.Equal(150, DvrScrubber.BehindForFrac(600, 0.75), 3);
    }

    [Fact]
    public void ShownBehindRoundTripsWithFrac()
    {
        var f = DvrScrubber.ShownFrac(1200, 0, dragFrac: 0.5, settleTargetBehindSec: null);
        Assert.Equal(600, DvrScrubber.ShownBehindSec(1200, f), 3);
    }

    [Theory]
    [InlineData(100, 2)]    // 2% of 100 = 2
    [InlineData(3600, 72)]  // 2% of 3600 = 72
    [InlineData(50, 2)]     // 2% of 50 = 1 → floored to 2
    public void SettleToleranceIsTwoPercentFlooredAtTwo(double window, double expected)
        => Assert.Equal(expected, DvrScrubber.SettleTolerance(window), 3);

    [Fact]
    public void IsSettledWhenPolledWithinTolerance()
    {
        Assert.True(DvrScrubber.IsSettled(3600, polledBehindSec: 350, targetBehindSec: 300));   // |50| ≤ 72
        Assert.False(DvrScrubber.IsSettled(3600, polledBehindSec: 500, targetBehindSec: 300));  // |200| > 72
    }

    [Theory]
    [InlineData(0.0, "● 直播即時")]
    [InlineData(2.9, "● 直播即時")]
    [InlineData(45, "落後直播 0:45")]
    [InlineData(3725, "落後直播 1:02:05")]
    public void PositionTextSwitchesAtLiveThreshold(double behind, string expected)
        => Assert.Equal(expected, DvrScrubber.PositionText(behind));

    [Fact]
    public void TickFractionsEmptyForShortWindows()
    {
        Assert.Empty(DvrScrubber.TickFractions(90));
        Assert.Empty(DvrScrubber.TickFractions(120));
    }

    [Fact]
    public void TickFractionsMinuteSpacingUnderTenMinutes()
    {
        // window 300 (>120, <600) → step 60 → behind 60,120,180,240 → fracs 0.8,0.6,0.4,0.2
        var ticks = DvrScrubber.TickFractions(300).Select(t => Math.Round(t, 3)).ToArray();
        Assert.Equal(new[] { 0.8, 0.6, 0.4, 0.2 }, ticks);
    }

    [Fact]
    public void TickFractionsHourSpacingForTwoHourPlusWindows()
    {
        // window 7200 (2h) → step 3600 → behind 3600 → frac 0.5
        var ticks = DvrScrubber.TickFractions(7200).Select(t => Math.Round(t, 3)).ToArray();
        Assert.Equal(new[] { 0.5 }, ticks);
    }

    [Fact]
    public void ParseProgressReadsBehindAndWindow()
    {
        var p = DvrScrubber.ParseProgress("{\"current\":540,\"duration\":0,\"seekableStart\":0,\"seekableEnd\":600}");
        Assert.NotNull(p);
        Assert.Equal(60, p!.Value.BehindLiveSec, 3);   // 600 − 540
        Assert.Equal(600, p.Value.DvrWindowSec, 3);    // 600 − 0
    }

    [Fact]
    public void ParseProgressToleratesWebView2DoubleEncoding()
    {
        // ExecuteScriptAsync returns a JS string as a quoted, escaped JSON string.
        var doubleEncoded = "\"{\\\"current\\\":300,\\\"seekableStart\\\":0,\\\"seekableEnd\\\":600}\"";
        var p = DvrScrubber.ParseProgress(doubleEncoded);
        Assert.NotNull(p);
        Assert.Equal(300, p!.Value.BehindLiveSec, 3);
        Assert.Equal(600, p.Value.DvrWindowSec, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("\"null\"")]
    [InlineData("{\"current\":0,\"seekableStart\":0,\"seekableEnd\":0}")]  // no window → not a live/DVR stream
    public void ParseProgressReturnsNullForNonLive(string? raw)
        => Assert.Null(DvrScrubber.ParseProgress(raw));
}
