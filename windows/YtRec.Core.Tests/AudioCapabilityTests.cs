using YtRec.Core;

namespace YtRec.Core.Tests;

public class AudioCapabilityTests
{
    [Theory]
    [InlineData(19045, AudioMode.SystemLoopback)]   // Win10 22H2 — no isolation
    [InlineData(20347, AudioMode.SystemLoopback)]   // just below the floor
    [InlineData(20348, AudioMode.PerProcessLoopback)] // documented floor
    [InlineData(22000, AudioMode.PerProcessLoopback)] // Win11 21H2
    [InlineData(22631, AudioMode.PerProcessLoopback)] // Win11 23H2
    public void ModeForBuild(int build, AudioMode expected)
    {
        Assert.Equal(expected, AudioCapability.ModeForBuild(build));
    }

    [Fact]
    public void IsolationOnlyAboveFloor()
    {
        Assert.False(AudioCapability.IsolatedAudioSupported(19045));
        Assert.True(AudioCapability.IsolatedAudioSupported(22000));
    }
}
