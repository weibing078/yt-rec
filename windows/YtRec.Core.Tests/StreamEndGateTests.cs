using YtRec.Core;

namespace YtRec.Core.Tests;

public class StreamEndGateTests
{
    [Fact]
    public void EndedWhileRecordingSchedulesOneStop()
    {
        Assert.Equal(StreamEndAction.ScheduleStop, StreamEndGate.OnEnded(isRecording: true, alreadyScheduled: false));
    }

    [Fact]
    public void EndedDuringPreviewDoesNotTearDown()
    {
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnEnded(isRecording: false, alreadyScheduled: false));
    }

    [Fact]
    public void RepeatedEndedDoesNotReschedule()
    {
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnEnded(isRecording: true, alreadyScheduled: true));
    }

    [Fact]
    public void ContentReadyCancelsPendingStop()
    {
        Assert.Equal(StreamEndAction.CancelScheduledStop,
            StreamEndGate.OnContentReady(contentReady: true, alreadyScheduled: true));
    }

    [Fact]
    public void ContentReadyWithoutPendingStopIsIgnored()
    {
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnContentReady(contentReady: true, alreadyScheduled: false));
        Assert.Equal(StreamEndAction.Ignore, StreamEndGate.OnContentReady(contentReady: false, alreadyScheduled: true));
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
