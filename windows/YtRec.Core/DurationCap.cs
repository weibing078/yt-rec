namespace YtRec.Core;

/// <summary>Recording duration-cap options (mirrors mac §8). SixHours is the default; Unlimited = no cap.
/// At the cap the recorder auto-finalizes + saves + notifies.</summary>
public enum DurationCap { ThreeHours, SixHours, TwelveHours, Unlimited }

public static class DurationCapExtensions
{
    /// <summary>The cap in seconds, or null for Unlimited.</summary>
    public static long? Seconds(this DurationCap cap) => cap switch
    {
        DurationCap.ThreeHours => 3 * 3600L,
        DurationCap.SixHours => 6 * 3600L,
        DurationCap.TwelveHours => 12 * 3600L,
        _ => null, // Unlimited
    };

    /// <summary>True once an in-progress recording has reached its cap. Unlimited never trips.</summary>
    public static bool ShouldAutoFinalize(this DurationCap cap, long elapsedSeconds)
        => cap.Seconds() is long limit && elapsedSeconds >= limit;
}
