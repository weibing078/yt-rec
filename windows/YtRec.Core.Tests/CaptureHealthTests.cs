using YtRec.Core;

namespace YtRec.Core.Tests;

public class CaptureHealthTests
{
    [Fact]
    public void SameSizeIsNotAChange()
        => Assert.False(CaptureHealth.SizeChanged(1920, 1080, 1920, 1080));

    [Fact]
    public void ADifferentSizeIsAChange()
        => Assert.True(CaptureHealth.SizeChanged(1920, 1080, 1280, 720));

    [Fact]
    public void ContentSizeIgnoresNoiseAndCatchesARealResize()
    {
        Assert.False(CaptureHealth.ContentSizeChanged(1920, 1080, 1921, 1080));
        Assert.False(CaptureHealth.ContentSizeChanged(1920, 1080, 0, 0));
        Assert.False(CaptureHealth.ContentSizeChanged(0, 0, 1920, 1080));
        Assert.True(CaptureHealth.ContentSizeChanged(1920, 1080, 1100, 700));
    }

    [Fact]
    public void SavedFileKeepsTheCaptureFaultAsTheNotice()
    {
        Assert.Equal(CaptureHealth.SizeChangedMessage, CaptureHealth.NoticeAfterSave(null, CaptureHealth.SizeChangedMessage));
        Assert.Equal("影片已結束", CaptureHealth.NoticeAfterSave("影片已結束", CaptureHealth.SizeChangedMessage));
        Assert.Null(CaptureHealth.NoticeAfterSave(null, null));
    }

    [Fact]
    public void NoPictureRectUsesTheNoNewFrameWarning()
    {
        Assert.Equal(CaptureHealth.PreviewStalledMessage, CaptureHealth.CropGiveUpMessage(98, 0));
        Assert.Contains("有範圍 3", CaptureHealth.CropGiveUpMessage(98, 3));
    }

    [Fact]
    public void PreviewStallNeedsEightSecondsWithoutANewFrame()
    {
        Assert.False(CaptureHealth.PreviewIsStalled(3, 3, 7999));
        Assert.True(CaptureHealth.PreviewIsStalled(3, 3, 8000));
        Assert.False(CaptureHealth.PreviewIsStalled(3, 4, 8000));
    }
}
