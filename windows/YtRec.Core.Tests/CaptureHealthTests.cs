using YtRec.Core;

namespace YtRec.Core.Tests;

public class CaptureHealthTests
{
    [Fact]
    public void HealthyWhileEarly()
    {
        Assert.Equal(CaptureHealth.Ok, HealthCheck.Evaluate(ticks: 3, completeFrames: 0, audioSamples: 0));
    }

    [Fact]
    public void NoFramesAfterEightSeconds()
    {
        Assert.Equal(CaptureHealth.NoFrames, HealthCheck.Evaluate(ticks: 4, completeFrames: 0, audioSamples: 0));
    }

    [Fact]
    public void AudioDisabledWhenFramesButNoAudio()
    {
        Assert.Equal(CaptureHealth.Ok, HealthCheck.Evaluate(ticks: 5, completeFrames: 10, audioSamples: 0)); // not yet
        Assert.Equal(CaptureHealth.AudioDisabled, HealthCheck.Evaluate(ticks: 6, completeFrames: 10, audioSamples: 0));
    }

    [Fact]
    public void OkWhenBothFlow()
    {
        Assert.Equal(CaptureHealth.Ok, HealthCheck.Evaluate(ticks: 10, completeFrames: 100, audioSamples: 200));
    }
}
