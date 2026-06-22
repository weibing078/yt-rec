using YtRec.Core;

namespace YtRec.Core.Tests;

public class PlayerAssetsTests
{
    [Fact]
    public void WatchUrlFromVariousForms()
    {
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            PlayerAssets.WatchUrlFrom("https://youtu.be/dQw4w9WgXcQ"));
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            PlayerAssets.WatchUrlFrom("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s"));
        Assert.Null(PlayerAssets.WatchUrlFrom("https://example.com/not-a-video"));
    }

    [Fact]
    public void BrowserArgumentsCarryAntiOcclusionFlags()
    {
        Assert.Contains("CalculateNativeWinOcclusion", PlayerAssets.BrowserArguments);
        Assert.Contains("--disable-renderer-backgrounding", PlayerAssets.BrowserArguments);
    }

    [Fact]
    public void SeekScriptIsInvariantFormatted()
    {
        Assert.Contains("seekTo(3599.5", PlayerAssets.SeekScript(3599.5));
    }

    [Fact]
    public void PlayerScriptStaysInlineTheaterForces1080AndReportsRectDims()
    {
        var s = PlayerAssets.FillPlayAndReportScript;
        Assert.DoesNotContain("100vw", s);                                 // NOT fullscreen-filled (would record black)
        Assert.Contains("ytp-size-button", s);                            // theater mode → large INLINE player
        Assert.Contains("setPlaybackQualityRange('hd1080', 'hd1080')", s); // 1080p pinned
        Assert.Contains("rect: [px / W", s);                              // crop to the PICTURE rect (no pillarbox)
        Assert.Contains("dims: [v.videoWidth, v.videoHeight]", s);         // source dims → orientation
        Assert.Contains("state: 'ended'", s);                             // stream-end signal
    }

    [Fact]
    public void PlayerScriptSkipsAdsAndGatesRecordingOnContent()
    {
        var s = PlayerAssets.FillPlayAndReportScript;
        Assert.Contains("ad-showing", s);            // detects an ad on the player
        Assert.Contains("ytp-ad-skip-button", s);    // auto-clicks the skip control
        Assert.Contains("ad: ad", s);                // reports ad state to the host
        Assert.Contains("ready: ready", s);          // reports content-ready so the host gates the writer
    }

    [Fact]
    public void SeekBehindScriptTargetsRelativeToLiveEdge()
    {
        var s = PlayerAssets.SeekBehindScript(120.5);
        Assert.Contains("getProgressState", s);
        Assert.Contains("seekableEnd-(120.5)", s);   // relative to the moving live edge, invariant-formatted
        Assert.Contains("seekTo(t,true)", s);
    }
}
