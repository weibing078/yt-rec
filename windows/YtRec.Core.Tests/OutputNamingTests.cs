using YtRec.Core;

namespace YtRec.Core.Tests;

// Naming parity with mac (AppState side-record 側錄_<title>.mp4 / recovery 側錄_上次未收工自動修復.mp4,
// FileUtil.sanitize). Deterministic char policy → identical on any host and always Windows-legal.
public class OutputNamingTests
{
    [Fact]
    public void SideRecordUsesSanitizedTitle()
    {
        Assert.Equal("側錄_我的直播.mp4", OutputNaming.SideRecordFileName("我的直播"));
        Assert.Equal("側錄_a b c.mp4", OutputNaming.SideRecordFileName("a/b:c")); // illegal → space
    }

    [Fact]
    public void SideRecordFallsBackWhenTitleBlank()
    {
        Assert.Equal("側錄.mp4", OutputNaming.SideRecordFileName(null));
        Assert.Equal("側錄.mp4", OutputNaming.SideRecordFileName("   "));
        // all-illegal collapses to 未命名 (matches mac), not the blank fallback
        Assert.Equal("側錄_未命名.mp4", OutputNaming.SideRecordFileName("///"));
    }

    [Fact]
    public void SanitizeReplacesEveryWindowsIllegalChar()
    {
        Assert.Equal("a b c d e f g h i j", OutputNaming.Sanitize("a<b>c:d\"e/f\\g|h?i*j")); // every reserved printable → space
        Assert.Equal("tab nl cr", OutputNaming.Sanitize("tab\tnl\ncr"));                      // control chars → space
    }

    [Fact]
    public void SanitizeTrimsEmptyAndCaps()
    {
        Assert.Equal("未命名", OutputNaming.Sanitize(""));
        Assert.Equal("未命名", OutputNaming.Sanitize("   "));
        Assert.Equal("hello", OutputNaming.Sanitize("  hello  "));
        Assert.Equal(60, OutputNaming.Sanitize(new string('x', 100)).Length); // capped at 60
    }

    [Fact]
    public void RecoveryNameIsFixed()
    {
        Assert.Equal("側錄_上次未收工自動修復.mp4", OutputNaming.RecoveryFileName);
    }
}
