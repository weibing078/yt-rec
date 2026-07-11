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

    // P1: a finalize interrupted by kill-9 / power loss leaves the reassembly temp files (never renamed onto
    // the real output). Those must NOT count as a finalized output, or recovery would skip the intact segments
    // forever and strand the user's footage.
    [Fact]
    public void InterruptedFinalizeTempsDoNotBlockRecovery()
    {
        var job = MakeJob("frag", init: true, segs: 3, final: false);
        var output = Path.Combine(job, "clip 側錄.mp4");
        File.WriteAllText(output + SegmentReassembler.ConcatTempSuffix, "frag");   // ".frag"
        File.WriteAllText(output + SegmentReassembler.OutputTempSuffix, "part");   // ".part"
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.False(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.Reassemble, RecoveryPlan.Decide(c));
    }

    [Fact]
    public void LegacyFragMp4TempDoesNotCountAsFinal()
    {
        // Older builds named the concat temp "<output>.mp4.frag.mp4", which DOES match the *.mp4 glob.
        var job = MakeJob("legacy", init: true, segs: 3, final: false);
        File.WriteAllText(Path.Combine(job, "clip 側錄.mp4.frag.mp4"), "frag");
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.False(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.Reassemble, RecoveryPlan.Decide(c));
    }

    [Fact]
    public void RealFinalizedOutputIsStillSkipped()
    {
        var job = MakeJob("real", init: true, segs: 3, final: false);
        File.WriteAllText(Path.Combine(job, "clip 側錄.mp4"), "video-bytes");
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.True(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.SkipAlreadyDone, RecoveryPlan.Decide(c));
    }

    [Theory]
    [InlineData("側錄_Movie.part1.mp4")]  // ".part" mid-name
    [InlineData("側錄_Teaser.part.mp4")]  // title ENDS in ".part" — must still be finalized
    [InlineData("側錄_Demo.frag.mp4")]    // title ENDS in ".frag" — only legacy ".mp4.frag.mp4" is a temp
    public void TitleContainingTempSuffixStillCountsAsFinal(string fileName)
    {
        // A video title may legitimately contain or end in ".part"/".frag" — the temp exclusion must match
        // only the legacy "<output>.mp4.frag.mp4" shape, or this finalized output would be "recovered"
        // (duplicated) on every launch.
        var job = MakeJob("titledot-" + fileName.Length, init: true, segs: 3, final: false);
        File.WriteAllText(Path.Combine(job, fileName), "video-bytes");
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.True(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.SkipAlreadyDone, RecoveryPlan.Decide(c));
    }

    // Stub runner for arg-shape tests: scripted result + creates the ffmpeg "output" so the atomic
    // promote (File.Move temp → final) succeeds.
    private sealed class ArgCapturingRunner : IProcessRunner
    {
        public IReadOnlyList<string>? Seen;
        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            string? workingDir = null, Action<string>? onLine = null, CancellationToken ct = default)
        {
            Seen = arguments;
            File.WriteAllText(arguments[^1], "muxed"); // last arg is the temp output path
            return Task.FromResult(new ProcessResult(0, "", false));
        }
    }

    [Fact]
    public async Task AudioMuxUsesShortestByDefault()
    {
        var job = MakeJob("shortest", init: true, segs: 3, final: false);
        var audio = SegmentReassembler.AudioPath(job);
        File.WriteAllBytes(audio, new byte[8]);
        var runner = new ArgCapturingRunner();
        var (ok, _) = await SegmentReassembler.ReassembleAsync(
            SegmentReassembler.SegmentsDir(job), Path.Combine(job, "out.mp4"), "ffmpeg", runner, audio);
        Assert.True(ok);
        Assert.Contains("-shortest", runner.Seen!);
    }

    [Fact]
    public async Task KeepFullVideoOmitsShortestSoAudioLossCannotTruncateVideo()
    {
        // Win10 mid-record default-device switch freezes audio.pcm early; -shortest would then silently trim
        // the whole video to the audio length. keepFullVideo must drop -shortest while still muxing the audio.
        var job = MakeJob("keepfull", init: true, segs: 3, final: false);
        var audio = SegmentReassembler.AudioPath(job);
        File.WriteAllBytes(audio, new byte[8]);
        var runner = new ArgCapturingRunner();
        var (ok, _) = await SegmentReassembler.ReassembleAsync(
            SegmentReassembler.SegmentsDir(job), Path.Combine(job, "out.mp4"), "ffmpeg", runner, audio,
            keepFullVideo: true);
        Assert.True(ok);
        Assert.DoesNotContain("-shortest", runner.Seen!);
        Assert.Contains("-c:a", runner.Seen!); // audio track is still muxed in
    }

    [Fact]
    public void EmptyMp4IsNotTreatedAsFinal()
    {
        // A zero-byte .mp4 is not a real output (size sanity) → recovery should still run.
        var job = MakeJob("empty", init: true, segs: 3, final: false);
        File.WriteAllText(Path.Combine(job, "clip 側錄.mp4"), "");
        var c = SegmentReassembler.Inspect(job, isRecording: false);
        Assert.False(c.HasFinalOutput);
        Assert.Equal(RecoveryAction.Reassemble, RecoveryPlan.Decide(c));
    }
}
