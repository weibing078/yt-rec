using YtRec.Core;

namespace YtRec.Core.Tests;

// Mirrors the mac §2 session-gate behavior: drop video before the first audio sample.
public class SessionGateTests
{
    [Fact]
    public void DropsVideoBeforeFirstAudio()
    {
        var gate = new SessionGate();
        Assert.False(gate.AudioStarted);
        Assert.False(gate.AcceptVideo(100)); // no audio yet → reject
        Assert.Null(gate.Retime(100));
    }

    [Fact]
    public void AnchorsToFirstAudioAndIsIdempotent()
    {
        var gate = new SessionGate();
        gate.OnAudioSample(500);
        gate.OnAudioSample(900); // later samples must not move the anchor
        Assert.True(gate.AudioStarted);
        Assert.Equal(500, gate.AnchorTick);
    }

    [Fact]
    public void AcceptsAndRetimesVideoAtOrAfterAnchor()
    {
        var gate = new SessionGate();
        gate.OnAudioSample(500);
        Assert.False(gate.AcceptVideo(499)); // pre-anchor frame dropped
        Assert.True(gate.AcceptVideo(500));  // exactly the anchor accepted
        Assert.Equal(0, gate.Retime(500));
        Assert.Equal(123, gate.Retime(623));
    }
}
