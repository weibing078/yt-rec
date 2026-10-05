using YtRec.Core;

namespace YtRec.Core.Tests;

/// <summary>Same scenarios as macOS StreamEndTableTests. Times are offsets from a fixed T0.</summary>
public class StreamEndTableTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    static PlayerSnapshot Snap(bool ended, bool ad, bool content, string id, double ageSeconds) =>
        new(ended, ad, content, id, T0.AddSeconds(-ageSeconds));

    static StreamDecision Eval(StreamPhase phase, string anchor, PlayerSnapshot? snap, double? candidateAge, int ext) =>
        StreamEndGate.Evaluate(phase, anchor, snap,
            candidateAge is double a ? T0.AddSeconds(-a) : null, ext, T0);

    [Fact]
    public void SameVideoResumesAfterBriefEnd_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 0), 5, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
        Assert.Null(d.CountdownSeconds);
    }

    [Fact]
    public void StillEndedAfter20Seconds_Stops()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, false, false, "aaa", 0), 20, 0);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void AutoplayNextVideoWithoutEnded_StopsNow()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "bbb", 0), null, 0);
        Assert.Equal(StreamEndGate.OtherVideoReason, d.StopReason);
    }

    [Fact]
    public void DifferentIdDuringAd_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, true, false, "bbb", 0), null, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
    }

    [Fact]
    public void AdAtDeadline_Extends()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, true, false, "aaa", 0), 20, 0);
        Assert.Null(d.StopReason);
        Assert.Equal(20, d.ExtendedAdSeconds);
        Assert.NotNull(d.CandidateStart);
    }

    [Fact]
    public void AdExtensionsReach120Seconds_ThenStops()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, true, false, "aaa", 0), 140, 120);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void EmptyIdWhileContentPlays_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "", 0), 5, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
    }

    [Fact]
    public void StaleSnapshotPastDeadline_Stops()
    {
        // Content is true on the stale snapshot; without R0 this would clear the candidate instead of stopping.
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 6), 20, 0);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void FinalizingIgnoresEnded()
    {
        var start = T0.AddSeconds(-5);
        var d = StreamEndGate.Evaluate(StreamPhase.Finalizing, "aaa", Snap(true, false, false, "aaa", 0), start, 0, T0);
        Assert.Null(d.StopReason);
        Assert.True(d.LeaveUiAlone);
        Assert.Equal(start, d.CandidateStart);
        Assert.False(d.PreviewShowsEnded);
    }

    [Fact]
    public void PreviewEndedShowsNoticeAndClearsWhenContentReturns()
    {
        var ended = Eval(StreamPhase.Preview, "aaa", Snap(true, false, false, "aaa", 0), null, 0);
        Assert.Null(ended.StopReason);
        Assert.True(ended.PreviewShowsEnded);
        Assert.False(StreamEndGate.CanBeginFromPreview(previewReady: true, previewShowsEnded: true));

        var back = Eval(StreamPhase.Preview, "aaa", Snap(false, false, true, "aaa", 0), null, 0);
        Assert.False(back.PreviewShowsEnded);
        Assert.True(StreamEndGate.CanBeginFromPreview(previewReady: true, previewShowsEnded: false));
    }

    [Fact]
    public void ContentWithoutCandidate_DoesNothing()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 0), null, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
        Assert.Null(d.CountdownSeconds);
    }
}

public class StreamEndGateTests
{
    [Fact]
    public void BeginRecordingFailsWhenPreviewIsGone()
    {
        Assert.False(StreamEndGate.BeginRecordingSucceeds(hasSession: false, isPreviewing: true, isRecording: false));
        Assert.False(StreamEndGate.BeginRecordingSucceeds(hasSession: true, isPreviewing: false, isRecording: false));
        Assert.False(StreamEndGate.BeginRecordingSucceeds(hasSession: true, isPreviewing: true, isRecording: true));
        Assert.True(StreamEndGate.BeginRecordingSucceeds(hasSession: true, isPreviewing: true, isRecording: false));
    }

    [Fact]
    public void StopStartsOnlyOnceAndFailsOnlyWhenTheSessionIsGone()
    {
        Assert.False(StreamEndGate.StopHasWork(hasSession: false, alreadyStopping: false));
        Assert.False(StreamEndGate.StopHasWork(hasSession: true, alreadyStopping: true));
        Assert.True(StreamEndGate.StopHasWork(hasSession: true, alreadyStopping: false));
        Assert.True(StreamEndGate.StopReportsFailure(hasSession: false));
        Assert.False(StreamEndGate.StopReportsFailure(hasSession: true));
    }
}
