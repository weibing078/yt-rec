using YtRec.Core;

namespace YtRec.Core.Tests;

public class CropStabilityTests
{
    [Fact]
    public void TwoIdenticalRectsAreStable()
        => Assert.True(CropStability.Same((0.1, 0.2, 0.8, 0.6), (0.1, 0.2, 0.8, 0.6)));

    [Fact]
    public void AMovedRectIsNotStable()
        => Assert.False(CropStability.Same((0.1, 0.2, 0.8, 0.6), (0.2, 0.2, 0.8, 0.6)));

    [Fact]
    public void AnEmptyRectIsNotStable()
        => Assert.False(CropStability.Same((0, 0, 0, 0), (0, 0, 0, 0)));
}
