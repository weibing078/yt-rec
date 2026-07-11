using YtRec.Core;

namespace YtRec.Core.Tests;

public class CfrPacingTests
{
    private const int Max = 60; // 2 s @ 30 fps

    [Fact]
    public void SteadyState_EmitsOnePerTick()
    {
        // Exactly due: elapsed == written → emit one, no skip.
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 100, written: 100, skipped: 0, Max);
        Assert.Equal(1, write);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void NotYetDue_EmitsNothing()
    {
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 99, written: 100, skipped: 0, Max);
        Assert.Equal(0, write);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void SmallGap_CatchesUpWithoutReanchoring()
    {
        // 10 frames behind → emit the inclusive 11 (…written..target), no skip.
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 110, written: 100, skipped: 0, Max);
        Assert.Equal(11, write);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void AtThreshold_DoesNotReanchor()
    {
        // due == Max is allowed to flush as a normal catch-up.
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 100 + Max - 1, written: 100, skipped: 0, Max);
        Assert.Equal(Max, write);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void HugeStall_ReanchorsAndEmitsOne()
    {
        // A multi-minute stall: emit a single fresh frame and absorb the gap into the accumulator.
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 100_000, written: 100, skipped: 0, Max);
        Assert.Equal(1, write);
        Assert.Equal(100_000 - 100, skipped); // target-written folded into skipped so next target == old written
    }

    [Fact]
    public void AfterReanchor_PacingResumesNormally()
    {
        // Reproduce the stall then continue: once re-anchored, the same raw clock must not re-burst, and as
        // the clock advances past the re-anchor point one-per-tick resumes.
        var (_, skipped) = CfrPacing.CatchUp(100_000, 100, 0, Max);

        // Same instant, the frame we just wrote is accounted (written now 101): nothing more due.
        var (w2, s2) = CfrPacing.CatchUp(100_000, 101, skipped, Max);
        Assert.Equal(0, w2);
        Assert.Equal(skipped, s2);

        // Clock advances 30 real frames past the re-anchor → 30 due, still no new skip.
        var (w3, s3) = CfrPacing.CatchUp(100_030, 101, skipped, Max);
        Assert.Equal(30, w3);
        Assert.Equal(skipped, s3);
    }

    [Fact]
    public void ZeroCeiling_NeverReanchors()
    {
        // Guard: a 0/negative ceiling disables the bound (defensive — the recorder always passes 2*fps).
        var (write, skipped) = CfrPacing.CatchUp(rawElapsedFrames: 5000, written: 0, skipped: 0, maxCatchUpFrames: 0);
        Assert.Equal(5001, write);
        Assert.Equal(0, skipped);
    }
}
