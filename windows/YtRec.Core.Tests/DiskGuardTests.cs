using YtRec.Core;

namespace YtRec.Core.Tests;

// Ported from mac PureLogicTests.swift (DiskGuardTests).
public class DiskGuardTests
{
    private const long Gb = 1_000_000_000;

    [Fact]
    public void PreCheck()
    {
        Assert.Equal(DiskPreCheck.Ok, DiskGuard.PreCheck(20 * Gb));
        Assert.Equal(DiskPreCheck.Warn(12 * Gb), DiskGuard.PreCheck(12 * Gb)); // 8–15GB warn
        Assert.Equal(DiskPreCheck.Refuse(5 * Gb), DiskGuard.PreCheck(5 * Gb)); // <8GB refuse
    }

    [Fact]
    public void StopRecording()
    {
        Assert.False(DiskGuard.ShouldStopRecording(12 * Gb));
        Assert.True(DiskGuard.ShouldStopRecording(8 * Gb)); // <10GB finalize
    }

    [Fact]
    public void PreCheckBoundaries()
    {
        Assert.Equal(DiskPreCheck.Warn(8 * Gb), DiskGuard.PreCheck(8 * Gb));   // exactly 8GB → warn
        Assert.Equal(DiskPreCheck.Ok, DiskGuard.PreCheck(15 * Gb));            // exactly 15GB → ok
        Assert.Equal(DiskPreCheck.Refuse(7 * Gb), DiskGuard.PreCheck(7 * Gb));
    }

    [Fact]
    public void StopRecordingBoundary()
    {
        Assert.False(DiskGuard.ShouldStopRecording(10 * Gb)); // exactly 10GB → keep
        Assert.True(DiskGuard.ShouldStopRecording(9 * Gb));
    }

    [Fact]
    public void FreeBytesUnknownDoesNotBlock()
    {
        Assert.Equal(long.MaxValue, DiskGuard.FreeBytes(() => null));
        Assert.Equal(DiskState.Ok, DiskGuard.PreCheck(DiskGuard.FreeBytes(() => null)).State);
    }
}
