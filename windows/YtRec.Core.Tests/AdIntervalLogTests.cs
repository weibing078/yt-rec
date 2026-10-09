using YtRec.Core;

namespace YtRec.Core.Tests;

public class AdIntervalLogTests
{
    [Fact]
    public void ZeroSpansRenderNothing()
    {
        var log = new AdIntervalLog();
        log.Observe(false, 10);
        log.CloseOpen(20);
        Assert.Null(log.Render());
    }

    [Fact]
    public void OneSpanUsesFileClockAndHeader()
    {
        var log = new AdIntervalLog();
        log.Observe(false, 0);
        log.Observe(true, 62);
        log.Observe(true, 63);
        log.Observe(false, 100);
        Assert.Equal("每秒偵測，誤差約 ±1-2 秒\n00:01:02–00:01:40\n", log.Render());
    }

    [Fact]
    public void StillOpenAtStopClosesOnTheLastSecond()
    {
        var log = new AdIntervalLog();
        log.Observe(true, 5);
        log.CloseOpen(8);
        Assert.Equal("每秒偵測，誤差約 ±1-2 秒\n00:00:05–00:00:08\n", log.Render());
    }

    [Fact]
    public void SidecarDropsMp4Extension()
        => Assert.Equal(
            Path.Combine("out", "側錄_標題.廣告時段.txt"),
            AdIntervalLog.SidecarPath(Path.Combine("out", "側錄_標題.mp4")));
}
