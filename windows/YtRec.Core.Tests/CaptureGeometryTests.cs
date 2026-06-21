using YtRec.Core;

namespace YtRec.Core.Tests;

/// <summary>L1 for the screen-independent capture-geometry contract (behavior-spec "Capture geometry").
/// Output size is a function of the SOURCE video + quality only; FitWindow is the Windows on-screen sizing.</summary>
public class CaptureGeometryTests
{
    [Theory]
    [InlineData(1920, 1080, Orientation.Landscape)] // 16:9
    [InlineData(3840, 2160, Orientation.Landscape)] // 4K landscape
    [InlineData(640, 480, Orientation.Landscape)]   // 4:3
    [InlineData(2560, 1080, Orientation.Landscape)] // ultrawide
    [InlineData(100, 100, Orientation.Landscape)]   // square → landscape
    [InlineData(1080, 1920, Orientation.Portrait)]  // 9:16
    [InlineData(405, 720, Orientation.Portrait)]    // shorts-ish
    [InlineData(0, 0, Orientation.Landscape)]       // unknown → landscape
    public void OrientationFromSourceDims(int w, int h, Orientation expected)
        => Assert.Equal(expected, CaptureGeometry.OrientationFor(w, h));

    [Theory]
    [InlineData(1920, 1080, 1080, 1920, 1080)] // landscape 1080
    [InlineData(1280, 720, 720, 1280, 720)]    // landscape 720
    [InlineData(3840, 2160, 1080, 1920, 1080)] // 4K source still 1080 out
    [InlineData(1080, 1920, 1080, 1080, 1920)] // portrait 1080
    [InlineData(405, 720, 720, 720, 1280)]     // portrait 720
    [InlineData(640, 480, 1080, 1920, 1080)]   // 4:3 → 16:9 target
    public void OutputSizeIsContentDriven(int vw, int vh, int quality, int ow, int oh)
    {
        var s = CaptureGeometry.OutputSize(vw, vh, quality);
        Assert.Equal((ow, oh), (s.Width, s.Height));
    }

    [Fact]
    public void OutputSizeAlwaysEven()
    {
        foreach (var (vw, vh) in new[] { (1920, 1080), (1080, 1920), (405, 720), (333, 777) })
        foreach (var q in new[] { 1080, 720 })
        {
            var s = CaptureGeometry.OutputSize(vw, vh, q);
            Assert.True(s.Width % 2 == 0 && s.Height % 2 == 0);
        }
    }

    [Theory]
    // landscape target on a screen that fits it exactly → unchanged
    [InlineData(1920, 1080, 1920, 1080, 1920, 1080)]
    // landscape target on a bigger screen → capped at target (never render larger than output)
    [InlineData(1920, 1080, 3840, 2160, 1920, 1080)]
    // landscape target on a sub-1080p screen → shrink preserving 16:9
    [InlineData(1920, 1080, 1366, 768, 1364, 768)]
    // portrait target on a 1080-tall screen → fit height, 9:16 → 606x1080 (floor-to-even)
    [InlineData(1080, 1920, 1920, 1080, 606, 1080)]
    public void FitWindowFitsScreenPreservingAspect(int tw, int th, int sw, int sh, int ww, int wh)
    {
        var win = CaptureGeometry.FitWindow(new CaptureSize(tw, th), sw, sh);
        Assert.Equal((ww, wh), (win.Width, win.Height));
        Assert.True(win.Width <= sw && win.Height <= sh, "window must fit on screen");
        Assert.True(win.Width % 2 == 0 && win.Height % 2 == 0, "even dims");
    }

    [Fact]
    public void FitWindowNeverExceedsTarget()
    {
        var win = CaptureGeometry.FitWindow(new CaptureSize(1920, 1080), 5000, 5000);
        Assert.True(win.Width <= 1920 && win.Height <= 1080);
    }
}
