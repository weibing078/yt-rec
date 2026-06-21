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
}
