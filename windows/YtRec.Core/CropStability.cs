namespace YtRec.Core;

/// <summary>The Windows crop rect must match twice before ffmpeg starts. A single fresh report can
/// still be the layout YouTube has not finished.</summary>
public static class CropStability
{
    public const double Epsilon = 0.002;

    public static bool Same(
        (double X, double Y, double W, double H) a,
        (double X, double Y, double W, double H) b)
    {
        if (a.W <= 0 || a.H <= 0 || b.W <= 0 || b.H <= 0) return false;
        return Math.Abs(a.X - b.X) < Epsilon
            && Math.Abs(a.Y - b.Y) < Epsilon
            && Math.Abs(a.W - b.W) < Epsilon
            && Math.Abs(a.H - b.H) < Epsilon;
    }
}
