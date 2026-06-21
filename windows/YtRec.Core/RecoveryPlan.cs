namespace YtRec.Core;

/// <summary>What to do with one job's <c>.work/segments/</c> dir on launch (mac §7).</summary>
public enum RecoveryAction
{
    Reassemble,       // init + ≥2 segments, no final output, not in use → rebuild + notify
    SkipInUse,        // the currently-recording job — never touch its segments
    SkipAlreadyDone,  // a final output already exists — idempotent, don't re-assemble/re-notify
    SkipNoInit,       // seg_init missing → abandon
    SkipTooFew,       // only 1 segment → nothing worth recovering
}

/// <summary>One recoverable-job candidate (filesystem facts gathered by the caller).</summary>
public readonly record struct RecoveryCandidate(
    string Dir, bool HasInit, int SegmentCount, bool HasFinalOutput, bool IsRecording);

/// <summary>Disaster-recovery decision (pure). The scan/concat IO lives in the Windows host; this is the
/// orchestration logic so it gets unit coverage with no filesystem. Idempotent and never touches in-use dirs.</summary>
public static class RecoveryPlan
{
    public static RecoveryAction Decide(RecoveryCandidate c)
    {
        if (c.IsRecording) return RecoveryAction.SkipInUse;        // never delete/rebuild in-use segments
        if (c.HasFinalOutput) return RecoveryAction.SkipAlreadyDone; // idempotent
        if (!c.HasInit) return RecoveryAction.SkipNoInit;
        if (c.SegmentCount < 2) return RecoveryAction.SkipTooFew;    // need init + ≥2 segments
        return RecoveryAction.Reassemble;
    }

    /// <summary>The dirs that should be reassembled this launch.</summary>
    public static IEnumerable<RecoveryCandidate> Recoverable(IEnumerable<RecoveryCandidate> all)
        => all.Where(c => Decide(c) == RecoveryAction.Reassemble);
}
