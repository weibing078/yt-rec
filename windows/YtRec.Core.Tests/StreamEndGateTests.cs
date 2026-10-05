using YtRec.Core;

namespace YtRec.Core.Tests;

public class StreamEndGateTests
{
    [Fact]
    public void EndedWhileRecordingSchedulesOneStop()
    {
        Assert.Equal(StreamEndAction.ScheduleStop,
            StreamEndGate.OnEnded(isRecording: true, alreadyScheduled: false, previewAlreadyNoted: false));
    }

    [Fact]
    public void EndedDuringPreviewNotesOnceWithoutTearingDown()
    {
        Assert.Equal(StreamEndAction.NotePreviewEnded,
            StreamEndGate.OnEnded(isRecording: false, alreadyScheduled: false, previewAlreadyNoted: false));
        Assert.Equal(StreamEndAction.Ignore,
            StreamEndGate.OnEnded(isRecording: false, alreadyScheduled: false, previewAlreadyNoted: true));
    }

    [Fact]
    public void RepeatedEndedDoesNotReschedule()
    {
        Assert.Equal(StreamEndAction.Ignore,
            StreamEndGate.OnEnded(isRecording: true, alreadyScheduled: true, previewAlreadyNoted: false));
    }

    [Fact]
    public void SameVideoAndReadyCancelsStop()
    {
        Assert.Equal(StreamEndAction.CancelScheduledStop, StreamEndGate.OnPlayback(
            alreadyScheduled: true, contentReady: true, isAd: false,
            scheduledVideoId: "abc", signalVideoId: "abc"));
    }

    [Fact]
    public void DifferentVideoStopsImmediately()
    {
        Assert.Equal(StreamEndAction.StopNow, StreamEndGate.OnPlayback(
            alreadyScheduled: true, contentReady: true, isAd: false,
            scheduledVideoId: "abc", signalVideoId: "xyz"));
        Assert.Equal(StreamEndAction.StopNow, StreamEndGate.OnPlayback(
            alreadyScheduled: true, contentReady: false, isAd: true,
            scheduledVideoId: "abc", signalVideoId: "xyz"));
    }

    [Fact]
    public void AdReadyDoesNotCancel()
    {
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnPlayback(
            alreadyScheduled: true, contentReady: true, isAd: true,
            scheduledVideoId: "abc", signalVideoId: "abc"));
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnPlayback(
            alreadyScheduled: true, contentReady: false, isAd: false,
            scheduledVideoId: "abc", signalVideoId: "abc"));
    }

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
