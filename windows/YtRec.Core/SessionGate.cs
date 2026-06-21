namespace YtRec.Core;

/// <summary>Session-gate — the single most important capture lesson (mac VERIFIED-BEHAVIOR §2).
/// YouTube starts playback late; if the first audio sample lands mid-stream it poisons the opening
/// fMP4 fragment and every reader (ffmpeg AND Media Foundation) then truncates the file and drops the
/// audio track. Fix: anchor the writer's session start to the FIRST audio sample and drop any video
/// frame that arrives before it. All timestamps share one QPC clock.</summary>
public sealed class SessionGate
{
    private readonly object _lock = new();
    private long? _anchorTick;

    /// <summary>Record the first audio sample's QPC tick (idempotent — only the first wins).</summary>
    public void OnAudioSample(long qpcTicks) { lock (_lock) _anchorTick ??= qpcTicks; }

    /// <summary>Has the first audio sample arrived (writing may begin)?</summary>
    public bool AudioStarted { get { lock (_lock) return _anchorTick.HasValue; } }

    /// <summary>The anchor tick (session zero), or null until the first audio sample.</summary>
    public long? AnchorTick { get { lock (_lock) return _anchorTick; } }

    /// <summary>Accept this video frame? Only once audio has started and the frame is at/after the anchor.</summary>
    public bool AcceptVideo(long qpcTicks) { lock (_lock) return _anchorTick is long a && qpcTicks >= a; }

    /// <summary>Presentation tick (relative to the anchor) for a sample, or null if it precedes the anchor.</summary>
    public long? Retime(long qpcTicks) { lock (_lock) return _anchorTick is long a && qpcTicks >= a ? qpcTicks - a : null; }
}
