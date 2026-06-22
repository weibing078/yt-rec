using System.Text.Json;

namespace YtRec.Core;

/// <summary>One parsed <c>latest.json</c> release manifest, for the platform that asked.</summary>
public sealed record UpdateManifest(string Version, string? Notes, string? Url, string Page);

/// <summary>Pure logic for the in-app update check (see shared/spec "App update check"): compare the running
/// version to the published one, and parse the hosted <c>latest.json</c> manifest. No I/O here — the app does
/// the fetch and the notice; this is the testable core.</summary>
public static class AppUpdate
{
    /// <summary>True when <paramref name="latest"/> is a strictly newer version than <paramref name="current"/>.
    /// Dotted-numeric compare (1.10 &gt; 1.9), missing parts = 0, a leading <c>v</c> and any <c>-pre</c>/<c>+meta</c>
    /// suffix are ignored. Unparseable input compares as 0 (never falsely offers an update).</summary>
    public static bool IsNewer(string? current, string? latest)
    {
        int[] c = Nums(current), l = Nums(latest);
        for (int i = 0; i < Math.Max(c.Length, l.Length); i++)
        {
            int cv = i < c.Length ? c[i] : 0, lv = i < l.Length ? l[i] : 0;
            if (lv != cv) return lv > cv;
        }
        return false;
    }

    private static int[] Nums(string? v)
    {
        var core = (v ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out nums[i]);
        return nums;
    }

    /// <summary>Parse the hosted manifest for one platform (<c>"mac"</c> / <c>"win"</c>) and UI language
    /// (falls back to <c>en</c> notes). Null if the JSON is missing/invalid.</summary>
    public static UpdateManifest? ParseManifest(string? json, string platform, string lang = "zh-Hant")
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("version", out var ver) || ver.GetString() is not string version) return null;

            string? notes = null;
            if (r.TryGetProperty("notes", out var n))
            {
                if (n.ValueKind == JsonValueKind.String) notes = n.GetString();
                else if (n.ValueKind == JsonValueKind.Object)
                    notes = (n.TryGetProperty(lang, out var ln) ? ln.GetString() : null)
                            ?? (n.TryGetProperty("en", out var en) ? en.GetString() : null);
            }

            string? url = r.TryGetProperty(platform, out var p) && p.ValueKind == JsonValueKind.Object
                          && p.TryGetProperty("url", out var u) ? u.GetString() : null;
            string page = r.TryGetProperty("page", out var pg) ? pg.GetString() ?? "" : "";
            return new UpdateManifest(version, notes, url, page);
        }
        catch { return null; }
    }
}
