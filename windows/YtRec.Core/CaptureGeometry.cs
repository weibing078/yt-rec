namespace YtRec.Core;

public enum Orientation { Landscape, Portrait }

public readonly record struct CaptureSize(int Width, int Height);

/// <summary>Pure capture geometry shared by both platforms (behavior-spec "Capture geometry"). The recorded
/// file's pixel size is a function of the SOURCE video + the quality setting only — never the host screen or
/// its DPI. That is what makes the output deterministic across machines. <see cref="FitWindow"/> is the
/// Windows-only on-screen sizing helper (macOS renders off-screen at the exact output size and needs no fit).</summary>
public static class CaptureGeometry
{
    /// <summary>Portrait when the source is strictly taller than wide; square, landscape and unknown
    /// (<paramref name="videoWidth"/> == 0) → Landscape.</summary>
    public static Orientation OrientationFor(int videoWidth, int videoHeight)
        => videoWidth > 0 && videoHeight > videoWidth ? Orientation.Portrait : Orientation.Landscape;

    /// <summary>Deterministic output size. <paramref name="quality"/> is the long-edge target (1080 default, or
    /// 720). Landscape → 1920×1080 / 1280×720; Portrait → 1080×1920 / 720×1280. Always even.</summary>
    public static CaptureSize OutputSize(int videoWidth, int videoHeight, int quality)
    {
        int shortEdge = quality == 720 ? 720 : 1080;
        int longEdge = shortEdge == 720 ? 1280 : 1920;
        return OrientationFor(videoWidth, videoHeight) == Orientation.Portrait
            ? new CaptureSize(shortEdge, longEdge)
            : new CaptureSize(longEdge, shortEdge);
    }

    /// <summary>Largest box of the target's aspect ratio that fits within the screen's physical pixels, capped
    /// at the target (no point rendering larger than we output). Floor-to-even so it never overhangs the screen
    /// edge and is yuv420p-safe; ffmpeg then scales+pads the captured frame up to the exact target.</summary>
    public static CaptureSize FitWindow(CaptureSize target, int screenWidth, int screenHeight)
    {
        double scale = 1.0;
        if (target.Width > screenWidth) scale = Math.Min(scale, (double)screenWidth / target.Width);
        if (target.Height > screenHeight) scale = Math.Min(scale, (double)screenHeight / target.Height);
        return new CaptureSize(Even(target.Width * scale), Even(target.Height * scale));
    }

    private static int Even(double v) => Math.Max(2, (int)Math.Floor(v) & ~1);
}
