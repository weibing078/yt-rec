namespace YtRec.Core;

/// <summary>Bounded constant-frame-rate catch-up (pure). The live recorder paces frames off a wall-clock:
/// each tick it emits the latest frame as many times as elapsed time says are due, so the file plays at true
/// speed. Left unbounded, a long capture stall (system sleep despite SleepPrevention, a lid-close, or a
/// lengthy WebView2 freeze) leaves the clock thousands of frames ahead and the recorder would dump every
/// duplicate in one burst — holding the capture lock (hanging Stop), bloating the file, and risking a full
/// disk. Past a threshold we instead treat the stall as a timeline discontinuity: skip the stalled span
/// (re-anchor the clock via the returned skipped-frame accumulator) and emit just the current frame.</summary>
public static class CfrPacing
{
    /// <summary>Given the raw elapsed-frame count (wall-clock seconds * fps), how many frames have already
    /// been written, the running skipped-frame accumulator, and the catch-up ceiling (seconds * fps), decide
    /// how many copies of the current frame to emit and the updated accumulator. Matches the inclusive
    /// <c>while (written &lt;= target)</c> pacing: a frame that is exactly due emits once.</summary>
    public static (int Write, long Skipped) CatchUp(long rawElapsedFrames, long written, long skipped, int maxCatchUpFrames)
    {
        long target = rawElapsedFrames - skipped;
        if (target < written) return (0, skipped);       // not yet due this tick
        long due = target - written + 1;                  // inclusive (target itself is due)
        if (maxCatchUpFrames > 0 && due > maxCatchUpFrames)
        {
            skipped += target - written;                  // re-anchor: drop the stalled gap, keep the current frame
            return (1, skipped);
        }
        return ((int)due, skipped);
    }
}
