using YtRec.Core;

namespace YtRec.Core.Tests;

public class LineCollectorTests
{
    [Fact]
    public void SplitsOnNewlineAndCarriageReturn()
    {
        var lines = new List<string>();
        var c = new LineCollector(lines.Add);
        c.Feed("a\rb\nc");   // \r (progress) and \n both break lines; "c" still buffered
        Assert.Equal(new[] { "a", "b" }, lines);
        c.Flush();
        Assert.Equal(new[] { "a", "b", "c" }, lines);
    }

    [Fact]
    public void FiltersBlankAndWhitespaceLines()
    {
        var lines = new List<string>();
        var c = new LineCollector(lines.Add);
        c.Feed("x\n\n   \n y \n");
        Assert.Equal(new[] { "x", "y" }, lines); // trimmed; blank/whitespace dropped
    }

    [Fact]
    public void KeepsLastTwoHundredLinesInTail()
    {
        var c = new LineCollector(null);
        for (var i = 0; i < 250; i++) c.Feed($"line{i}\n");
        var tail = c.Tail().Split('\n');
        Assert.Equal(200, tail.Length);
        Assert.Equal("line50", tail[0]);
        Assert.Equal("line249", tail[^1]);
    }
}
