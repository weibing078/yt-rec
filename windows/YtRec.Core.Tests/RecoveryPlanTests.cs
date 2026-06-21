using YtRec.Core;

namespace YtRec.Core.Tests;

public class RecoveryPlanTests
{
    private static RecoveryCandidate C(bool init = true, int segs = 5, bool final = false, bool rec = false)
        => new("job", init, segs, final, rec);

    [Fact]
    public void ReassemblesCompleteOrphan()
    {
        Assert.Equal(RecoveryAction.Reassemble, RecoveryPlan.Decide(C()));
    }

    [Fact]
    public void NeverTouchesInUseDir()
    {
        // even a complete-looking dir is skipped while it is the recording job
        Assert.Equal(RecoveryAction.SkipInUse, RecoveryPlan.Decide(C(rec: true)));
    }

    [Fact]
    public void IdempotentOnAlreadyFinalized()
    {
        Assert.Equal(RecoveryAction.SkipAlreadyDone, RecoveryPlan.Decide(C(final: true)));
    }

    [Fact]
    public void AbandonsWithoutInit()
    {
        Assert.Equal(RecoveryAction.SkipNoInit, RecoveryPlan.Decide(C(init: false)));
    }

    [Fact]
    public void SkipsTooFewSegments()
    {
        Assert.Equal(RecoveryAction.SkipTooFew, RecoveryPlan.Decide(C(segs: 1)));
    }

    [Fact]
    public void RecoverableFiltersTheBatch()
    {
        var all = new[] { C(), C(rec: true), C(final: true), C(init: false), C(segs: 1) };
        Assert.Single(RecoveryPlan.Recoverable(all));
    }
}
