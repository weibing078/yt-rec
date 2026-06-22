using System.Text.Json;

namespace YtRec.Core;

/// <summary>Live position read off the YouTube player: how far behind the live edge, and the rewindable window.</summary>
public readonly record struct DvrProgress(double BehindLiveSec, double DvrWindowSec);

/// <summary>Pure math for the live-rewind timeline scrubber (mac monitor-UI parity, extracted here for L1
/// tests — see shared/spec "Live rewind scrubber"). Fraction 0 = oldest rewindable point, 1 = live edge.</summary>
public static class DvrScrubber
{
    /// <summary>Within this many seconds of the live edge counts as "at live".</summary>
    public const double LiveThresholdSec = 3.0;

    /// <summary>A DVR window of this length or shorter is too short to position in → no scrubber.</summary>
    public const double MinScrubWindowSec = 90.0;

    /// <summary>Guarded window length (never divide by zero).</summary>
    public static double Window(double dvrWindowSec) => Math.Max(1, dvrWindowSec);

    /// <summary>Show the scrubber only when the rewindable window is worth positioning in.</summary>
    public static bool CanScrub(double dvrWindowSec) => dvrWindowSec > MinScrubWindowSec;

    /// <summary>Where the live edge currently sits on the bar (1 = live, 0 = oldest).</summary>
    public static double LiveFrac(double dvrWindowSec, double behindLiveSec)
        => Math.Clamp(1 - behindLiveSec / Window(dvrWindowSec), 0, 1);

    /// <summary>Where the knob is drawn: the live drag value while dragging, else a held release target,
    /// else the live position.</summary>
    public static double ShownFrac(double dvrWindowSec, double behindLiveSec, double? dragFrac, double? settleTargetBehindSec)
    {
        if (dragFrac is double d) return Math.Clamp(d, 0, 1);
        if (settleTargetBehindSec is double t) return Math.Clamp(1 - t / Window(dvrWindowSec), 0, 1);
        return LiveFrac(dvrWindowSec, behindLiveSec);
    }

    /// <summary>Seconds-behind-live the knob currently represents.</summary>
    public static double ShownBehindSec(double dvrWindowSec, double shownFrac)
        => Math.Max(0, (1 - shownFrac) * Window(dvrWindowSec));

    /// <summary>Seconds-behind-live a scrubber fraction maps to (for the seek call).</summary>
    public static double BehindForFrac(double dvrWindowSec, double frac)
        => Math.Max(0, (1 - Math.Clamp(frac, 0, 1)) * Window(dvrWindowSec));

    /// <summary>How close a fresh poll must land to the release target before the hold is released.</summary>
    public static double SettleTolerance(double dvrWindowSec) => Math.Max(2, Window(dvrWindowSec) * 0.02);

    /// <summary>The drag-release "hold" resolves when a polled position lands within tolerance of the target.</summary>
    public static bool IsSettled(double dvrWindowSec, double polledBehindSec, double targetBehindSec)
        => Math.Abs(polledBehindSec - targetBehindSec) <= SettleTolerance(dvrWindowSec);

    /// <summary>Position readout: "● 直播即時" near the edge, else "落後直播 M:SS".</summary>
    public static string PositionText(double shownBehindSec)
        => shownBehindSec < LiveThresholdSec ? "● 直播即時" : $"落後直播 {Timecode.FormatShort(shownBehindSec)}";

    /// <summary>Tick-mark fractions (0 = oldest .. 1 = live) at hour / 10-min / 1-min spacing; empty for short
    /// windows. Ticks are measured outward from the live edge.</summary>
    public static IReadOnlyList<double> TickFractions(double dvrWindowSec)
    {
        double w = Window(dvrWindowSec);
        if (w <= 120) return Array.Empty<double>();
        double step = w >= 2 * 3600 ? 3600 : (w >= 600 ? 600 : 60);
        var ticks = new List<double>();
        for (double t = step; t < w; t += step) ticks.Add(1 - t / w);
        return ticks;
    }

    /// <summary>Parse the player's <c>getProgressState()</c> JSON into behind-live + DVR-window seconds.
    /// Tolerates WebView2's double-encoding (a returned JS string comes back as a quoted JSON string) and the
    /// literal <c>null</c>. Returns null when there's no usable seekable range (not a live/DVR stream yet).</summary>
    public static DvrProgress? ParseProgress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw!.Trim();
        try
        {
            if (s.Length > 0 && s[0] == '"') s = JsonSerializer.Deserialize<string>(s) ?? "";
            s = s.Trim();
            if (s.Length == 0 || s == "null") return null;
            using var doc = JsonDocument.Parse(s);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            double current = Num(r, "current"), end = Num(r, "seekableEnd"), start = Num(r, "seekableStart");
            if (end <= 0) return null;
            return new DvrProgress(Math.Max(0, end - current), Math.Max(0, end - start));
        }
        catch { return null; }

        static double Num(JsonElement r, string k)
            => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }
}
