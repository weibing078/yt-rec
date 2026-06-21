namespace YtRec.Core;

/// <summary>Capture health verdict (mac §8). NoFrames = likely window minimized; AudioDisabled =
/// frames flow but no audio, so fall back to video-only survival mode.</summary>
public enum CaptureHealth { Ok, NoFrames, AudioDisabled }

/// <summary>Health check (pure). A tick is a ~2 s poll: ~8 s (ticks ≥ 4) with 0 complete frames → warn;
/// ~12 s (ticks ≥ 6) with frames but no audio → video-only survival mode.</summary>
public static class HealthCheck
{
    public const int NoFrameTicks = 4;       // ~8 s
    public const int NoAudioTicks = 6;       // ~12 s

    public static CaptureHealth Evaluate(int ticks, int completeFrames, int audioSamples)
    {
        if (ticks >= NoFrameTicks && completeFrames == 0) return CaptureHealth.NoFrames;
        if (ticks >= NoAudioTicks && completeFrames > 0 && audioSamples == 0) return CaptureHealth.AudioDisabled;
        return CaptureHealth.Ok;
    }
}
