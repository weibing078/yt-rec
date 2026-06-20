namespace YtRec.Core;

public enum DiskState { Ok, Warn, Refuse }

/// <summary>Result of a pre-recording disk check. Carries the free-byte count for Warn/Refuse.</summary>
public readonly record struct DiskPreCheck(DiskState State, long FreeBytes)
{
    public static readonly DiskPreCheck Ok = new(DiskState.Ok, 0);
    public static DiskPreCheck Warn(long bytes) => new(DiskState.Warn, bytes);
    public static DiskPreCheck Refuse(long bytes) => new(DiskState.Refuse, bytes);
}

/// <summary>Disk protection (pure logic). Decimal GB to match Explorer/Finder. Mirrors mac DiskGuard.swift.</summary>
public static class DiskGuard
{
    public const double Gb = 1_000_000_000.0;

    public static DiskPreCheck PreCheck(long freeBytes, double warnGB = 15, double refuseGB = 8)
    {
        var free = freeBytes / Gb;
        if (free < refuseGB) return DiskPreCheck.Refuse(freeBytes);
        if (free < warnGB) return DiskPreCheck.Warn(freeBytes);
        return DiskPreCheck.Ok;
    }

    /// <summary>During recording: stop &amp; finalize if free space drops below the stop threshold.</summary>
    public static bool ShouldStopRecording(long freeBytes, double stopGB = 10) =>
        freeBytes / Gb < stopGB;

    /// <summary>Injectable free-bytes resolver: null (unknown) → long.MaxValue so we never falsely block.</summary>
    public static long FreeBytes(Func<long?> resolver) => resolver() ?? long.MaxValue;
}
