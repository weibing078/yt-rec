namespace YtRec.Core;

/// <summary>Pure helpers to shrink a captured BGRA frame down to a small image the floating viewfinder can
/// upload cheaply. The full capture is up to 1080p (~8 MB/frame); pushing that into the on-screen preview made
/// it lag — every <c>WriteableBitmap.Invalidate()</c> re-uploads the whole bitmap to the compositor. The
/// monitor is only ~360 px wide, so we downscale to a small box first: the per-frame UI write drops ~10× and
/// the preview keeps up with real time. The recording itself is untouched (it writes the full-res crop).</summary>
public static class PreviewScaler
{
    /// <summary>Largest preview edge in pixels. Past this the small viewfinder gains no visible detail but pays
    /// the full upload cost; 640 stays crisp even if the user enlarges the monitor a bit.</summary>
    public const int MaxEdge = 640;

    /// <summary>Even target dimensions that fit a <paramref name="srcW"/>×<paramref name="srcH"/> frame inside a
    /// <paramref name="maxEdge"/> box, preserving aspect. Never upscales; clamps to ≥2 and trims to even (so a
    /// later yuv/encode step never sees an odd dimension).</summary>
    public static (int W, int H) FitBox(int srcW, int srcH, int maxEdge = MaxEdge)
    {
        if (srcW <= 0 || srcH <= 0) return (2, 2);
        int longest = Math.Max(srcW, srcH);
        double s = longest > maxEdge ? (double)maxEdge / longest : 1.0;
        int w = Math.Max(2, (int)Math.Round(srcW * s)) & ~1;
        int h = Math.Max(2, (int)Math.Round(srcH * s)) & ~1;
        return (w, h);
    }

    /// <summary>Nearest-neighbour downscale of a tightly-packed BGRA buffer (stride = <paramref name="srcW"/>×4)
    /// into a fresh <paramref name="dstW"/>×<paramref name="dstH"/> BGRA buffer. Cheap by design: it reads only
    /// dst-many pixels, never the whole source. Good enough for a confidence monitor.</summary>
    public static byte[] DownscaleBgra(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        if (srcW <= 0 || srcH <= 0) return dst;
        for (int y = 0; y < dstH; y++)
        {
            int sy = (int)((long)y * srcH / dstH);
            int srcRow = sy * srcW * 4, dstRow = y * dstW * 4;
            for (int x = 0; x < dstW; x++)
            {
                int si = srcRow + (int)((long)x * srcW / dstW) * 4, di = dstRow + x * 4;
                dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
            }
        }
        return dst;
    }
}
