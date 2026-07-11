using YtRec.Core;

namespace YtRec.Core.Tests;

// Off-screen player park point (Win32PlayerHost.OffscreenOrigin). Invariant: origin inside a real monitor,
// body hangs off the virtual desktop's bottom-right — so it can't render on a second monitor and survives a
// mid-recording resolution/layout change.
public class OffscreenParkTests
{
    private static ScreenRect R(int l, int t, int r, int b) => new(l, t, r, b);

    [Fact]
    public void SingleMonitorParksAtBottomRightSliver()
    {
        var (x, y) = OffscreenPark.Origin(new[] { R(0, 0, 1920, 1080) });
        Assert.Equal((1918, 1078), (x, y)); // == old (sw-2, sh-2): preserves the a36fbf7 single-monitor behavior
    }

    [Fact]
    public void SecondMonitorToTheRightParksOffTheRightmost()
    {
        // primary + a monitor to the right (same top). Old code parked at primary's corner (1918,1078),
        // which is inside the RIGHT monitor's visible area. New code parks off the rightmost.
        var (x, y) = OffscreenPark.Origin(new[] { R(0, 0, 1920, 1080), R(1920, 0, 3840, 1080) });
        Assert.Equal((3838, 1078), (x, y));
    }

    [Fact]
    public void MonitorBelowParksOffTheLowest()
    {
        var (x, y) = OffscreenPark.Origin(new[] { R(0, 0, 1920, 1080), R(0, 1080, 1920, 2160) });
        Assert.Equal((1918, 2158), (x, y)); // lowest bottom wins
    }

    [Fact]
    public void LowestBottomWinsThenRightmostBreaksTies()
    {
        // Two monitors share the lowest bottom (2160); the rightmost of those is chosen.
        var mons = new[] { R(0, 0, 1920, 2160), R(1920, 0, 3840, 2160), R(0, 0, 2560, 1440) };
        var (x, y) = OffscreenPark.Origin(mons);
        Assert.Equal((3838, 2158), (x, y));
    }

    [Fact]
    public void EmptyListFallsBackToZero()
    {
        Assert.Equal((0, 0), OffscreenPark.Origin(System.Array.Empty<ScreenRect>()));
    }
}
