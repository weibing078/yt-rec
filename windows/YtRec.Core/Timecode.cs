using System.Globalization;

namespace YtRec.Core;

/// <summary>Timecode parse/format for "grab a VOD section" (pure logic). Mirrors mac Timecode.swift.</summary>
public static class Timecode
{
    /// <summary>Parse "90" / "5:30" / "1:05:30" / "1:30.5" → seconds; null if invalid.</summary>
    public static double? Parse(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) return null;
        var parts = s.Split(':'); // empty subsequences kept: "5:" -> ["5",""]
        if (parts.Length is < 1 or > 3) return null;

        var values = new List<double>(parts.Length);
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
                return null;
            values.Add(v);
        }
        return values.Count switch
        {
            1 => values[0],
            2 => values[0] * 60 + values[1],
            _ => values[0] * 3600 + values[1] * 60 + values[2],
        };
    }

    /// <summary>Seconds → "HH:MM:SS" (for yt-dlp).</summary>
    public static string Format(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds, MidpointRounding.AwayFromZero));
        return $"{total / 3600:D2}:{total % 3600 / 60:D2}:{total % 60:D2}";
    }

    /// <summary>Seconds → short display ("M:SS", or "H:MM:SS" past an hour).</summary>
    public static string FormatShort(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds, MidpointRounding.AwayFromZero));
        int h = total / 3600, m = total % 3600 / 60, sec = total % 60;
        return h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m}:{sec:D2}";
    }

    /// <summary>yt-dlp --download-sections "*START-END"; requires 0 ≤ start &lt; end, else null.</summary>
    public static string? DownloadSectionArg(double startSec, double endSec)
    {
        if (startSec < 0 || endSec <= startSec) return null;
        return $"*{Format(startSec)}-{Format(endSec)}";
    }

    /// <summary>From two raw strings → (arg, human label), both must be valid and start &lt; end.</summary>
    public static (string Arg, string Label)? Section(string startRaw, string endRaw)
    {
        var a = Parse(startRaw);
        var b = Parse(endRaw);
        if (a is null || b is null) return null;
        var arg = DownloadSectionArg(a.Value, b.Value);
        if (arg is null) return null;
        return (arg, $"{FormatShort(a.Value)}–{FormatShort(b.Value)}");
    }
}
