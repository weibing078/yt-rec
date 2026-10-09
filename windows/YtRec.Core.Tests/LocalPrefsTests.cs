using YtRec.Core;

namespace YtRec.Core.Tests;

public class LocalPrefsTests
{
    [Theory]
    [InlineData(3, DurationCap.ThreeHours)]
    [InlineData(6, DurationCap.SixHours)]
    [InlineData(12, DurationCap.TwelveHours)]
    [InlineData(0, DurationCap.Unlimited)]
    [InlineData(99, DurationCap.SixHours)]
    public void DurationRoundTrip(int hours, DurationCap expected)
    {
        var json = LocalPrefs.WriteDuration(LocalPrefs.DurationFromHours(hours));
        Assert.Equal(expected, LocalPrefs.ReadDuration(json));
    }

    [Fact]
    public void QualityAndOutputFolderRoundTripAndKeepTheOldRoot()
    {
        var earlier = LocalPrefs.EarlierOutputDirs(@"C:\vid\YT-Rec", Array.Empty<string>(), @"C:\new\YT-Rec");
        Assert.Equal(Path.GetFullPath(@"C:\vid\YT-Rec"), earlier[0]);
        var roots = LocalPrefs.RecoveryRoots(@"C:\new\YT-Rec", earlier);
        Assert.Equal(Path.GetFullPath(@"C:\new\YT-Rec"), roots[0]);
        Assert.Contains(Path.GetFullPath(@"C:\vid\YT-Rec"), roots);

        var json = LocalPrefs.WriteSettings(new SavedSettings(DurationCap.ThreeHours, 720, @"C:\new\YT-Rec", earlier));
        var read = LocalPrefs.ReadSettings(json);
        Assert.Equal(DurationCap.ThreeHours, read.Duration);
        Assert.Equal(720, read.Quality);
        Assert.Equal(@"C:\new\YT-Rec", read.OutputDir);
        Assert.Equal(1080, LocalPrefs.NormalizeQuality(999));
    }

    [Fact]
    public void EmptyDownloadFolderIsDiscardedWhenSwitchingToSideRecord()
    {
        Assert.True(LocalPrefs.ShouldDiscardEmptyDownloadDir(0));
        Assert.False(LocalPrefs.ShouldDiscardEmptyDownloadDir(1));
        Assert.Equal("這支不是已結束的影片（直播中或尚未開播），已幫你切到側錄預覽", LocalPrefs.SwitchedToSideRecord);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void BadDurationJsonStaysAtSixHours(string? json)
        => Assert.Equal(DurationCap.SixHours, LocalPrefs.ReadDuration(json));

    [Fact]
    public void RecentListKeepsFiveNewestAndDropsMissing()
    {
        var current = new[]
        {
            new StoredRecent(@"C:\v\a.mp4", "Sidecar"),
            new StoredRecent(@"C:\v\gone.mp4", "Sidecar"),
            new StoredRecent(@"C:\v\b.mp4", "Native"),
        };
        bool Exists(string path) => path != @"C:\v\gone.mp4";
        var next = LocalPrefs.RememberRecent(current, new StoredRecent(@"C:\v\c.mp4", "Sidecar"), Exists);
        Assert.Equal(new[] { @"C:\v\c.mp4", @"C:\v\a.mp4", @"C:\v\b.mp4" }, next.Select(x => x.Path));
    }

    [Fact]
    public void RecentListCapsAtFiveAndReplacesSamePath()
    {
        var current = Enumerable.Range(1, 5)
            .Select(i => new StoredRecent($@"C:\v\{i}.mp4", "Sidecar"))
            .ToArray();
        var replaced = LocalPrefs.RememberRecent(current, new StoredRecent(@"C:\v\1.mp4", "Native"), _ => true);
        Assert.Equal(5, replaced.Count);
        Assert.Equal("Native", replaced[0].Kind);
        Assert.Equal(1, replaced.Count(x => x.Path == @"C:\v\1.mp4"));

        var capped = LocalPrefs.RememberRecent(current, new StoredRecent(@"C:\v\6.mp4", "Sidecar"), _ => true);
        Assert.Equal(5, capped.Count);
        Assert.Equal(@"C:\v\6.mp4", capped[0].Path);
        Assert.DoesNotContain(capped, x => x.Path == @"C:\v\5.mp4");
    }

    [Fact]
    public void LoadRecentSkipsDeletedFiles()
    {
        var json = LocalPrefs.WriteRecent(new[]
        {
            new StoredRecent(@"C:\v\keep.mp4", "Sidecar"),
            new StoredRecent(@"C:\v\gone.mp4", "Native"),
        });
        var loaded = LocalPrefs.ReadRecent(json, path => path.EndsWith("keep.mp4"));
        Assert.Equal(new[] { @"C:\v\keep.mp4" }, loaded.Select(x => x.Path));
    }
}
