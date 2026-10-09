using YtRec.Core;

namespace YtRec.Core.Tests;

public class AppUpdateTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]   // same → no update
    [InlineData("1.1.0", "1.0.9", false)]   // older latest → no
    [InlineData("1.0", "1.0.1", true)]      // missing patch = 0
    [InlineData("1.2.3", "1.10.0", true)]   // numeric, not lexical (10 > 2)
    [InlineData("v1.0.0", "v1.2.0", true)]  // tolerate a leading v
    [InlineData("1.0.0", "1.0.0-beta", false)] // pre-release suffix ignored on equal core
    [InlineData("", "1.0.0", true)]         // unknown current → offer
    [InlineData("1.0.0", "", false)]        // unparseable latest → never offer
    public void IsNewerComparesNumerically(string current, string latest, bool expected)
        => Assert.Equal(expected, AppUpdate.IsNewer(current, latest));

    [Fact]
    public void ParseManifestReadsVersionUrlAndLocalizedNotes()
    {
        var json = """
        {"version":"1.1.0","pubDate":"2026-06-22",
         "notes":{"zh-Hant":"倒帶預覽","en":"Rewind preview"},
         "mac":{"url":"https://x/Y.dmg","minOS":"14.4"},
         "win":{"url":"https://x/Y.zip"},
         "page":"https://ytrec.example/#download"}
        """;
        var mac = AppUpdate.ParseManifest(json, "mac", "zh-Hant");
        Assert.NotNull(mac);
        Assert.Equal("1.1.0", mac!.Version);
        Assert.Equal("倒帶預覽", mac.Notes);
        Assert.Equal("https://x/Y.dmg", mac.Url);
        Assert.Equal("https://ytrec.example/#download", mac.Page);

        var win = AppUpdate.ParseManifest(json, "win", "ja"); // unknown lang → en fallback
        Assert.Equal("Rewind preview", win!.Notes);
        Assert.Equal("https://x/Y.zip", win.Url);
    }

    [Theory]
    [InlineData("https://github.com/weibing078/yt-rec/releases/download/v1.1.2/YT-Rec.zip", null, "https://github.com/weibing078/yt-rec/releases/download/v1.1.2/YT-Rec.zip")]
    [InlineData("https://github.com/weibing078/yt-rec/releases", null, "https://github.com/weibing078/yt-rec/releases")]
    [InlineData("https://evil.example/payload.exe", "https://ytrec.resonaframe.com/download", "https://ytrec.resonaframe.com/download")]
    [InlineData("http://github.com/weibing078/yt-rec/releases/x.zip", "http://ytrec.resonaframe.com/", "https://ytrec.resonaframe.com/")]
    [InlineData("https://github.com/weibing078/other/releases/x.zip", "https://evil.example/", "https://ytrec.resonaframe.com/")]
    [InlineData("https://github.com.evil.com/weibing078/yt-rec/releases/x.zip", null, "https://ytrec.resonaframe.com/")]
    [InlineData("https://user:pw@github.com/weibing078/yt-rec/releases/x.zip", null, "https://ytrec.resonaframe.com/")]
    [InlineData("file:///etc/passwd", null, "https://ytrec.resonaframe.com/")]
    public void SafeOpenUrlLocksPath(string? url, string? page, string expected)
        => Assert.Equal(expected, AppUpdate.SafeOpenUrl(url, page));

    [Fact]
    public void OpenableRejectsATamperedStoredLink()
        => Assert.Equal(AppUpdate.Homepage, AppUpdate.Openable("https://github.com/someone/else/releases/x.zip"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"pubDate\":\"x\"}")] // no version
    public void ParseManifestNullForBadInput(string? json)
        => Assert.Null(AppUpdate.ParseManifest(json, "mac"));
}
