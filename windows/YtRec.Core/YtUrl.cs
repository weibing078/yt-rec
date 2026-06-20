using System.Text.RegularExpressions;

namespace YtRec.Core;

/// <summary>YouTube URL parsing (pure logic). Mirrors mac YtURL.swift.</summary>
public static class YtUrl
{
    private const string IdPattern = "^[A-Za-z0-9_-]{11}$";

    public static bool IsProbablyYouTube(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var url)) return false;
        var host = url.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be");
    }

    /// <summary>Extract the 11-char video ID from any YouTube URL form; null if none.</summary>
    public static string? VideoId(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var url)) return null;

        static string? Valid(string? c) =>
            c != null && Regex.IsMatch(c, IdPattern) ? c : null;

        var host = url.Host.ToLowerInvariant();
        if (host.Contains("youtu.be"))
            return Valid(url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());

        // ?v= takes precedence over path segments
        foreach (var kv in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = kv.IndexOf('=');
            if (eq > 0 && kv[..eq] == "v")
            {
                var id = Valid(Uri.UnescapeDataString(kv[(eq + 1)..]));
                if (id != null) return id;
            }
        }

        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "live" or "shorts" or "embed" or "v" && i + 1 < parts.Length)
            {
                var id = Valid(parts[i + 1]);
                if (id != null) return id;
            }
        }
        return null;
    }
}
