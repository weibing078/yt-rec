using YtRec.Core;

namespace YtRec.Core.Tests;

public class DurationCapTests
{
    [Fact]
    public void Seconds()
    {
        Assert.Equal(3 * 3600L, DurationCap.ThreeHours.Seconds());
        Assert.Equal(6 * 3600L, DurationCap.SixHours.Seconds());
        Assert.Equal(12 * 3600L, DurationCap.TwelveHours.Seconds());
        Assert.Null(DurationCap.Unlimited.Seconds());
    }

    [Fact]
    public void AutoFinalizeAtCap()
    {
        Assert.False(DurationCap.SixHours.ShouldAutoFinalize(6 * 3600 - 1));
        Assert.True(DurationCap.SixHours.ShouldAutoFinalize(6 * 3600));      // exactly at cap → finalize
        Assert.True(DurationCap.ThreeHours.ShouldAutoFinalize(3 * 3600 + 5));
    }

    [Fact]
    public void UnlimitedNeverFinalizes()
    {
        Assert.False(DurationCap.Unlimited.ShouldAutoFinalize(99 * 3600));
    }
}
