using YtRec.Core;

namespace YtRec.Core.Tests;

// Ported from mac PureLogicTests.swift (TimecodeTests).
public class TimecodeTests
{
    [Fact]
    public void ParseSeconds()
    {
        Assert.Equal(90, Timecode.Parse("90"));
        Assert.Equal(0, Timecode.Parse("0"));
        Assert.Equal(12, Timecode.Parse(" 12 "));
    }

    [Fact]
    public void ParseMinuteSecond()
    {
        Assert.Equal(330, Timecode.Parse("5:30"));
        Assert.Equal(5, Timecode.Parse("0:05"));
    }

    [Fact]
    public void ParseHourMinuteSecond()
    {
        Assert.Equal(3930, Timecode.Parse("1:05:30"));
        Assert.Equal(7200, Timecode.Parse("2:00:00"));
    }

    [Fact]
    public void ParseInvalid()
    {
        Assert.Null(Timecode.Parse(""));
        Assert.Null(Timecode.Parse("abc"));
        Assert.Null(Timecode.Parse("5:"));      // empty segment
        Assert.Null(Timecode.Parse("1:2:3:4")); // >3 segments
        Assert.Null(Timecode.Parse("-5"));      // negative
    }

    [Fact]
    public void FormatRoundTrip()
    {
        Assert.Equal("01:05:30", Timecode.Format(3930));
        Assert.Equal("00:00:05", Timecode.Format(5));
    }

    [Fact]
    public void DownloadSectionArg()
    {
        Assert.Equal("*00:05:30-00:07:45", Timecode.DownloadSectionArg(330, 465));
        Assert.Null(Timecode.DownloadSectionArg(100, 100));
        Assert.Null(Timecode.DownloadSectionArg(200, 100));
    }

    [Fact]
    public void SectionFromRawStrings()
    {
        var s = Timecode.Section("5:30", "7:45");
        Assert.Equal("*00:05:30-00:07:45", s?.Arg);
        Assert.Equal("5:30–7:45", s?.Label);
        Assert.Null(Timecode.Section("5:30", "abc"));
        Assert.Null(Timecode.Section("7:45", "5:30")); // reversed
    }
}
