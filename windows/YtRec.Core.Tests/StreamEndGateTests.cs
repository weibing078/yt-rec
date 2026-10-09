using YtRec.Core;

namespace YtRec.Core.Tests;

/// <summary>Same scenarios as macOS StreamEndTableTests. Times are a monotonic clock, not wall time.</summary>
public class StreamEndTableTests
{
    static PlayerSnapshot Snap(bool ended, bool ad, bool content, string id, double age, TimeSpan now) =>
        new(ended, ad, content, id, now - TimeSpan.FromSeconds(age));

    static StreamDecision Eval(StreamPhase phase, string anchor, PlayerSnapshot? snap,
        double? candidateAge, int ext, int streak, double nowSeconds = 1000) 
    {
        var now = TimeSpan.FromSeconds(nowSeconds);
        return StreamEndGate.Evaluate(phase, anchor, snap,
            candidateAge is double a ? now - TimeSpan.FromSeconds(a) : null,
            ext, streak, now);
    }

    [Fact]
    public void PreviewContentReadyNeedsFreshNonAdContent()
    {
        var now = TimeSpan.FromSeconds(1000);
        Assert.True(StreamEndGate.PreviewContentReady(Snap(false, false, true, "aaa", 0, now), now));
        Assert.False(StreamEndGate.PreviewContentReady(Snap(false, true, true, "aaa", 0, now), now));
        Assert.False(StreamEndGate.PreviewContentReady(Snap(false, false, true, "aaa", 6, now), now));
        Assert.False(StreamEndGate.PreviewContentReady(null, now));
    }

    [Fact]
    public void EndedStartsCountdown()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, false, false, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.Null(d.StopReason);
        Assert.NotNull(d.CandidateStart);
        Assert.Equal(20, d.CountdownSeconds);
    }

    [Fact]
    public void AdEndedDoesNotStartCountdown()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, true, false, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
    }

    [Fact]
    public void PreviewAdEndedDoesNotShowEnded()
    {
        var d = Eval(StreamPhase.Preview, "aaa", Snap(true, true, false, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.False(d.PreviewShowsEnded);
        Assert.True(StreamEndGate.CanBeginFromPreview(true, d.PreviewShowsEnded, d.PreviewShowsOtherVideo));
    }

    [Fact]
    public void SameVideoResumesAfterBriefEnd_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 0, TimeSpan.FromSeconds(1000)), 5, 40, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
        Assert.Equal(0, d.ExtendedAdSeconds);
    }

    [Fact]
    public void CountdownSecondsMatchTimeLeft()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, false, false, "aaa", 0, TimeSpan.FromSeconds(1000)), 5, 0, 0);
        Assert.Equal(15, d.CountdownSeconds);
        Assert.Null(d.StopReason);
    }

    [Fact]
    public void StillEndedAfter20Seconds_Stops()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, false, false, "aaa", 0, TimeSpan.FromSeconds(1000)), 20, 0, 0);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void OtherVideoNeedsThreeFreshSnapshots()
    {
        var now = TimeSpan.FromSeconds(1000);
        var snap = Snap(false, false, true, "bbb", 0, now);
        var d1 = StreamEndGate.Evaluate(StreamPhase.Recording, "aaa", snap, null, 0, 0, now);
        Assert.Null(d1.StopReason);
        Assert.Equal(1, d1.OtherVideoStreak);
        var d2 = StreamEndGate.Evaluate(StreamPhase.Recording, d1.AnchorId, snap, null, 0, d1.OtherVideoStreak, now);
        Assert.Null(d2.StopReason);
        Assert.Equal(2, d2.OtherVideoStreak);
        var same = Snap(false, false, true, "aaa", 0, now);
        var reset = StreamEndGate.Evaluate(StreamPhase.Recording, "aaa", same, null, 0, 2, now);
        Assert.Null(reset.StopReason);
        Assert.Equal(0, reset.OtherVideoStreak);
        var again = StreamEndGate.Evaluate(StreamPhase.Recording, "aaa", snap, null, 0, 0, now);
        var third = StreamEndGate.Evaluate(StreamPhase.Recording, "aaa", snap, null, 0, 2, now);
        Assert.Equal(1, again.OtherVideoStreak);
        Assert.Equal(StreamEndGate.OtherVideoReason, third.StopReason);
    }

    [Fact]
    public void DifferentIdDuringAd_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, true, false, "bbb", 0, TimeSpan.FromSeconds(1000)), null, 0, 2);
        Assert.Null(d.StopReason);
        Assert.Equal(0, d.OtherVideoStreak);
    }

    [Fact]
    public void AdAtDeadline_Extends()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, true, false, "aaa", 0, TimeSpan.FromSeconds(1000)), 20, 0, 0);
        Assert.Null(d.StopReason);
        Assert.Equal(20, d.ExtendedAdSeconds);
        Assert.NotNull(d.CandidateStart);
    }

    [Fact]
    public void AdExtensionsReach120Seconds_ThenStops()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(true, true, false, "aaa", 0, TimeSpan.FromSeconds(1000)), 140, 120, 0);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void EmptyIdWhileContentPlays_DoesNotStop()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "", 0, TimeSpan.FromSeconds(1000)), 5, 0, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
    }

    [Fact]
    public void StaleSnapshotClearsIdAndAd()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, true, false, "bbb", 6, TimeSpan.FromSeconds(1000)), null, 0, 2);
        Assert.Null(d.StopReason);
        Assert.Equal(0, d.OtherVideoStreak);
    }

    [Fact]
    public void StaleSnapshotPastDeadline_Stops()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 6, TimeSpan.FromSeconds(1000)), 20, 0, 0);
        Assert.Equal(StreamEndGate.VideoEndedReason, d.StopReason);
    }

    [Fact]
    public void EmptyAnchorAdoptsFirstContentId_AndDoesNotStopBeforeThat()
    {
        var idle = Eval(StreamPhase.Preview, "", Snap(false, false, false, "zzz", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        var idle3 = Eval(StreamPhase.Preview, "", Snap(false, false, false, "zzz", 0, TimeSpan.FromSeconds(1000)), null, 0, 2);
        Assert.Equal("", idle.AnchorId);
        Assert.Equal(0, idle3.OtherVideoStreak);
        Assert.Null(idle3.StopReason);
        Assert.False(idle3.PreviewShowsOtherVideo);

        var adopted = Eval(StreamPhase.Preview, "", Snap(false, false, true, "xyz", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.Equal("xyz", adopted.AnchorId);
        Assert.Null(adopted.StopReason);
    }

    [Fact]
    public void PreviewOtherVideoBlocksBeginUntilItReturns()
    {
        var now = TimeSpan.FromSeconds(1000);
        var other = Snap(false, false, true, "bbb", 0, now);
        var d2 = StreamEndGate.Evaluate(StreamPhase.Preview, "aaa", other, null, 0, 2, now);
        Assert.True(d2.PreviewShowsOtherVideo);
        Assert.Null(d2.StopReason);
        Assert.False(StreamEndGate.CanBeginFromPreview(true, d2.PreviewShowsEnded, d2.PreviewShowsOtherVideo));

        var back = Snap(false, false, true, "aaa", 0, now);
        var restored = StreamEndGate.Evaluate(StreamPhase.Preview, "aaa", back, null, 0, d2.OtherVideoStreak, now);
        Assert.False(restored.PreviewShowsOtherVideo);
        Assert.True(StreamEndGate.CanBeginFromPreview(true, restored.PreviewShowsEnded, restored.PreviewShowsOtherVideo));
    }

    [Fact]
    public void PreviewEndedShowsNoticeAndClearsWhenContentReturns()
    {
        var ended = Eval(StreamPhase.Preview, "aaa", Snap(true, false, false, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.Null(ended.StopReason);
        Assert.True(ended.PreviewShowsEnded);
        Assert.False(StreamEndGate.CanBeginFromPreview(true, ended.PreviewShowsEnded, ended.PreviewShowsOtherVideo));

        var back = Eval(StreamPhase.Preview, "aaa", Snap(false, false, true, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.False(back.PreviewShowsEnded);
        Assert.True(StreamEndGate.CanBeginFromPreview(true, back.PreviewShowsEnded, back.PreviewShowsOtherVideo));
    }

    [Fact]
    public void ContentWithoutCandidate_DoesNothing()
    {
        var d = Eval(StreamPhase.Recording, "aaa", Snap(false, false, true, "aaa", 0, TimeSpan.FromSeconds(1000)), null, 0, 0);
        Assert.Null(d.StopReason);
        Assert.Null(d.CandidateStart);
        Assert.Null(d.CountdownSeconds);
    }
}

public class SnapParseTests
{
    [Fact]
    public void ParsesACompleteSnapshot()
    {
        var s = StreamEndGate.ParseSnapJson("{\"ended\":true,\"ad\":false,\"content\":true,\"id\":\"abc\"}");
        Assert.True(s.Ended);
        Assert.False(s.Ad);
        Assert.True(s.Content);
        Assert.Equal("abc", s.Id);
    }

    [Fact]
    public void MissingWrongOrBrokenFieldsStayUnknown()
    {
        Assert.Equal(ParsedSnap.Unknown, StreamEndGate.ParseSnapJson(null));
        Assert.Equal(ParsedSnap.Unknown, StreamEndGate.ParseSnapJson("not json"));
        Assert.Equal(ParsedSnap.Unknown, StreamEndGate.ParseSnapJson("[]"));
        Assert.Equal(ParsedSnap.Unknown, StreamEndGate.ParseSnapJson("{}"));
        Assert.Equal(ParsedSnap.Unknown, StreamEndGate.ParseSnapJson("{\"ended\":1,\"ad\":\"yes\",\"content\":false,\"id\":12}"));
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
