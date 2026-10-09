namespace YtRec.Core;

/// <summary>x264 rate cap for the Windows writer. CRF stays 23; 720p (long side ≤ 1280) is 6M/12M,
/// anything larger is 12M/24M. This is a ceiling, not an average bitrate.</summary>
public static class X264Rate
{
    public static string[] LimitArgs(int outWidth, int outHeight)
    {
        int longer = Math.Max(outWidth, outHeight);
        var (max, buf) = longer <= 1280 ? ("6M", "12M") : ("12M", "24M");
        return new[] { "-crf", "23", "-maxrate", max, "-bufsize", buf };
    }
}
