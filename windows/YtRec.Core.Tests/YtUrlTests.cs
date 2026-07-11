using YtRec.Core;

namespace YtRec.Core.Tests;

// Ported from mac Tests/YTRecTests/PureLogicTests.swift (YtURLTests).
public class YtUrlTests
{
    [Fact]
    public void WatchUrl()
    {
        Assert.Equal("dQw4w9WgXcQ", YtUrl.VideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.Equal("dQw4w9WgXcQ", YtUrl.VideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s"));
    }

    [Fact]
    public void ShortAndLiveUrl()
    {
        Assert.Equal("jNQXAC9IVRw", YtUrl.VideoId("https://youtu.be/jNQXAC9IVRw"));
        Assert.Equal("abcDEF12345", YtUrl.VideoId("https://www.youtube.com/live/abcDEF12345?feature=share"));
        Assert.Equal("abcDEF12345", YtUrl.VideoId("https://www.youtube.com/shorts/abcDEF12345"));
    }

    [Fact]
    public void Invalid()
    {
        Assert.Null(YtUrl.VideoId("https://example.com/watch?v=dQw4w9WgXcQx"));
        Assert.False(YtUrl.IsProbablyYouTube("https://vimeo.com/123"));
        Assert.True(YtUrl.IsProbablyYouTube("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
    }

    [Fact]
    public void EmbedAndVPaths()
    {
        Assert.Equal("abcDEF12345", YtUrl.VideoId("https://www.youtube.com/embed/abcDEF12345"));
        Assert.Equal("abcDEF12345", YtUrl.VideoId("https://www.youtube.com/v/abcDEF12345"));
    }

    [Fact]
    public void IdLengthBoundary()
    {
        Assert.Null(YtUrl.VideoId("https://www.youtube.com/watch?v=abcDEF1234"));   // 10 chars
        Assert.Null(YtUrl.VideoId("https://youtu.be/abcDEF123456"));                // 12 chars
        Assert.Equal("ab-DE_1234x", YtUrl.VideoId("https://www.youtube.com/watch?v=ab-DE_1234x")); // - _ legal
    }

    [Fact]
    public void SubdomainsAreYouTube()
    {
        Assert.True(YtUrl.IsProbablyYouTube("https://m.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.True(YtUrl.IsProbablyYouTube("https://music.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.True(YtUrl.IsProbablyYouTube("https://youtu.be/jNQXAC9IVRw"));
    }

    [Fact]
    public void LeadingWhitespaceTrimmed()
    {
        Assert.Equal("jNQXAC9IVRw", YtUrl.VideoId("  https://youtu.be/jNQXAC9IVRw \n"));
    }

    // Security: a spoofed host (substring-bypass of the old host.Contains check) must not yield an id, or the
    // raw attacker-controlled URL would be handed straight to yt-dlp (SSRF + hostile parse surface).
    [Fact]
    public void SpoofedHostIsRejected()
    {
        Assert.Null(YtUrl.VideoId("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ"));
        Assert.Null(YtUrl.VideoId("https://notyoutube.com/watch?v=dQw4w9WgXcQ"));
        Assert.Null(YtUrl.VideoId("https://youtu.be.evil.com/dQw4w9WgXcQ"));
        Assert.False(YtUrl.IsProbablyYouTube("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ"));
        Assert.False(YtUrl.IsProbablyYouTube("https://notyoutube.com/"));
        // Genuine subdomains still resolve.
        Assert.Equal("dQw4w9WgXcQ", YtUrl.VideoId("https://gaming.youtube.com/watch?v=dQw4w9WgXcQ"));
    }
}
