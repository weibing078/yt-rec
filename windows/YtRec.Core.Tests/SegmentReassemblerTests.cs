using YtRec.Core;

namespace YtRec.Core.Tests;

// L2: real temp filesystem, no ffmpeg. Exercises Inspect + the RecoveryPlan integration.
public class SegmentReassemblerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ytrec-seg-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_base, recursive: true); } catch { } }

    private string MakeJob(string name, bool init, int segs, bool final)
    {
        var job = Path.Combine(_base, name);
        var segDir = SegmentReassembler.SegmentsDir(job);
        Directory.CreateDirectory(segDir);
        if (init) File.WriteAllText(Path.Combine(segDir, SegmentReassembler.InitName), "init");
        for (var i = 0; i < segs; i++) File.WriteAllText(Path.Combine(segDir, $"seg_{i:00000}.m4s"), "seg");
        if (final) File.WriteAllText(Path.Combine(job, "output.mp4"), "done");
        return job;
    }

    [Fact]
    public void InspectReadsSegmentFacts()
    {
        var job = MakeJob("a", init: true, segs: 3, final: false);
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.True(c.HasInit);
        Assert.Equal(3, c.SegmentCount);
        Assert.False(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.Reassemble, RecoveryPlan.Decide(c));
    }

    [Fact]
    public void FinalizedJobIsSkipped()
    {
        var job = MakeJob("b", init: true, segs: 4, final: true);
        Assert.Equal(RecoveryAction.SkipAlreadyDone, RecoveryPlan.Decide(SegmentReassembler.Inspect(job, false)));
    }

    [Fact]
    public void ActiveJobIsNeverTouched()
    {
        var job = MakeJob("c", init: true, segs: 4, final: false);
        Assert.Equal(RecoveryAction.SkipInUse, RecoveryPlan.Decide(SegmentReassembler.Inspect(job, isRecording: true)));
    }

    [Fact]
    public void MissingInitIsAbandoned()
    {
        var job = MakeJob("d", init: false, segs: 4, final: false);
        Assert.Equal(RecoveryAction.SkipNoInit, RecoveryPlan.Decide(SegmentReassembler.Inspect(job, false)));
    }

    [Fact]
    public async Task ReassembleRejectsTooFewSegments()
    {
        var job = MakeJob("e", init: true, segs: 1, final: false);
        var (ok, err) = await SegmentReassembler.ReassembleAsync(
            SegmentReassembler.SegmentsDir(job), Path.Combine(job, "output.mp4"),
            "ffmpeg", new ProcessRunner());
        Assert.False(ok);
        Assert.Equal("too few segments", err);
    }
}
