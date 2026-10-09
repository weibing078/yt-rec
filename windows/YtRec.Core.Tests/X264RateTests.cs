using YtRec.Core;

namespace YtRec.Core.Tests;

public class X264RateTests
{
    [Fact]
    public void Portrait1080UsesThe12MegabitCeiling()
    {
        var args = X264Rate.LimitArgs(1080, 1920);
        Assert.Equal(new[] { "-crf", "23", "-maxrate", "12M", "-bufsize", "24M" }, args);
    }

    [Fact]
    public void Landscape720UsesThe6MegabitCeiling()
    {
        var args = X264Rate.LimitArgs(1280, 720);
        Assert.Equal(new[] { "-crf", "23", "-maxrate", "6M", "-bufsize", "12M" }, args);
    }
}
