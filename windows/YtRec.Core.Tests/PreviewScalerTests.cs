using YtRec.Core;

namespace YtRec.Core.Tests;

public class PreviewScalerTests
{
    [Theory]
    [InlineData(1920, 1080, 640, 360)]  // 16:9 landscape → fits long edge to 640
    [InlineData(1080, 1920, 360, 640)]  // 9:16 portrait → fits long edge to 640
    [InlineData(1280, 720, 640, 360)]
    [InlineData(640, 360, 640, 360)]    // already at cap → unchanged
    [InlineData(320, 240, 320, 240)]    // smaller than cap → never upscaled
    public void FitBoxScalesIntoTheBoxKeepingAspect(int sw, int sh, int ew, int eh)
        => Assert.Equal((ew, eh), PreviewScaler.FitBox(sw, sh));

    [Fact]
    public void FitBoxAlwaysReturnsEvenDimensions()
    {
        var (w, h) = PreviewScaler.FitBox(1001, 667); // odd source, scaled
        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    public void FitBoxGuardsDegenerateInput(int sw, int sh)
        => Assert.Equal((2, 2), PreviewScaler.FitBox(sw, sh));

    [Fact]
    public void DownscaleProducesCorrectlySizedBuffer()
    {
        var src = new byte[1920 * 1080 * 4];
        var dst = PreviewScaler.DownscaleBgra(src, 1920, 1080, 640, 360);
        Assert.Equal(640 * 360 * 4, dst.Length);
    }

    [Fact]
    public void DownscaleHalvingPicksNearestSourcePixels()
    {
        // 2x2 source, distinct BGRA per pixel; halving to 1x1 takes the top-left (nearest at 0,0).
        var src = new byte[]
        {
            10, 11, 12, 13,   20, 21, 22, 23,
            30, 31, 32, 33,   40, 41, 42, 43,
        };
        var dst = PreviewScaler.DownscaleBgra(src, 2, 2, 1, 1);
        Assert.Equal(new byte[] { 10, 11, 12, 13 }, dst);
    }

    [Fact]
    public void DownscaleIdentityKeepsPixelsWhenSizeUnchanged()
    {
        var src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; // 2x1 BGRA
        var dst = PreviewScaler.DownscaleBgra(src, 2, 1, 2, 1);
        Assert.Equal(src, dst);
    }
}
