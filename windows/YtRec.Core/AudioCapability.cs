namespace YtRec.Core;

/// <summary>How audio can be captured on this machine.</summary>
public enum AudioMode
{
    /// <summary>WASAPI process-loopback: records ONLY the player's process tree (zero bleed). Win11 only.</summary>
    PerProcessLoopback,
    /// <summary>Default-render-endpoint loopback: records ALL system audio. Win10 fallback — surfaced in the
    /// UI as "this PC can't isolate audio" (ADR-0004).</summary>
    SystemLoopback,
}

/// <summary>Audio-capability gate (pure). Per-window isolation needs ActivateAudioInterfaceAsync process-
/// loopback, whose documented floor is build 20348 — effectively Windows 11 (consumer Win10 22H2 = 19045
/// does NOT have it; verified). Below the floor we fall back to whole-system loopback.</summary>
public static class AudioCapability
{
    /// <summary>Documented process-loopback floor (Windows Server 2022 / Win11 era). Consumer machines step
    /// from 19045 (Win10 22H2) straight to 22000 (Win11), so this cleanly separates the two.</summary>
    public const int ProcessLoopbackMinBuild = 20348;

    public static AudioMode ModeForBuild(int osBuild) =>
        osBuild >= ProcessLoopbackMinBuild ? AudioMode.PerProcessLoopback : AudioMode.SystemLoopback;

    /// <summary>True if this build can isolate one app's audio (no bleed from other apps).</summary>
    public static bool IsolatedAudioSupported(int osBuild) => ModeForBuild(osBuild) == AudioMode.PerProcessLoopback;
}
