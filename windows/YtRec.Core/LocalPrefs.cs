using System.Text.Json;

namespace YtRec.Core;

/// <summary>One recent output remembered across launches. Kind is the <c>FileKind</c> name.</summary>
public sealed record StoredRecent(string Path, string Kind);

/// <summary>Duration cap and the recent-file list, as plain JSON. Missing files are dropped on load.
/// The list stays at five, newest first, matching the Windows window.</summary>
public static class LocalPrefs
{
    public const int RecentMax = 5;
    public const string SwitchedToSideRecord = "這支不是已結束的影片（直播中或尚未開播），已幫你切到側錄預覽";

    /// <summary>The download folder created before a live handoff is removed only when nothing was written.</summary>
    public static bool ShouldDiscardEmptyDownloadDir(int entryCount) => entryCount == 0;

    public static int DurationHours(DurationCap cap) => cap switch
    {
        DurationCap.ThreeHours => 3,
        DurationCap.TwelveHours => 12,
        DurationCap.Unlimited => 0,
        _ => 6,
    };

    public static DurationCap DurationFromHours(int hours) => hours switch
    {
        3 => DurationCap.ThreeHours,
        12 => DurationCap.TwelveHours,
        0 => DurationCap.Unlimited,
        _ => DurationCap.SixHours,
    };

    public static string WriteDuration(DurationCap cap)
        => JsonSerializer.Serialize(new DurationDto(DurationHours(cap)));

    public static DurationCap ReadDuration(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DurationCap.SixHours;
        try
        {
            var dto = JsonSerializer.Deserialize<DurationDto>(json);
            return dto is null ? DurationCap.SixHours : DurationFromHours(dto.DurationHours);
        }
        catch { return DurationCap.SixHours; }
    }

    public static string WriteRecent(IReadOnlyList<StoredRecent> items)
        => JsonSerializer.Serialize(items);

    public static IReadOnlyList<StoredRecent> ReadRecent(string? json, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<StoredRecent>();
        try
        {
            var items = JsonSerializer.Deserialize<List<StoredRecent>>(json) ?? new List<StoredRecent>();
            return items.Where(i => !string.IsNullOrWhiteSpace(i.Path) && fileExists(i.Path)).Take(RecentMax).ToArray();
        }
        catch { return Array.Empty<StoredRecent>(); }
    }

    /// <summary>Newest stays even if the caller has not flushed it to disk yet. Older rows must still exist,
    /// and the same path is not listed twice.</summary>
    public static IReadOnlyList<StoredRecent> RememberRecent(
        IReadOnlyList<StoredRecent> current, StoredRecent newest, Func<string, bool> fileExists)
    {
        var next = new List<StoredRecent>();
        if (!string.IsNullOrWhiteSpace(newest.Path)) next.Add(newest);
        foreach (var old in current)
        {
            if (next.Count >= RecentMax) break;
            if (string.IsNullOrWhiteSpace(old.Path)) continue;
            if (next.Any(x => string.Equals(x.Path, old.Path, StringComparison.OrdinalIgnoreCase))) continue;
            if (!fileExists(old.Path)) continue;
            next.Add(old);
        }
        return next;
    }

    public static int NormalizeQuality(int quality) => quality == 720 ? 720 : 1080;

    /// <summary>Current folder first, then earlier folders, without duplicates. Recovery scans every one.</summary>
    public static IReadOnlyList<string> RecoveryRoots(string? current, IEnumerable<string>? earlier)
    {
        var list = new List<string>();
        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            string full;
            try { full = Path.GetFullPath(raw); }
            catch { return; }
            if (list.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(full);
        }
        Add(current);
        if (earlier != null) foreach (var path in earlier) Add(path);
        return list;
    }

    /// <summary>After the user picks <paramref name="next"/>, the previous current folder stays on the
    /// recovery list so an unfinished recording there can still be repaired.</summary>
    public static IReadOnlyList<string> EarlierOutputDirs(string? current, IReadOnlyList<string> earlier, string next)
    {
        var prior = new List<string>();
        if (!string.IsNullOrWhiteSpace(current)) prior.Add(current);
        prior.AddRange(earlier);
        return RecoveryRoots(next, prior).Skip(1).ToArray();
    }

    public static string WriteSettings(SavedSettings settings)
        => JsonSerializer.Serialize(new SettingsDto(
            DurationHours(settings.Duration),
            NormalizeQuality(settings.Quality),
            settings.OutputDir,
            settings.EarlierOutputDirs.ToArray()));

    public static SavedSettings ReadSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SavedSettings(DurationCap.SixHours, 1080, null, Array.Empty<string>());
        try
        {
            var dto = JsonSerializer.Deserialize<SettingsDto>(json);
            if (dto is null) return new SavedSettings(DurationCap.SixHours, 1080, null, Array.Empty<string>());
            return new SavedSettings(
                DurationFromHours(dto.DurationHours),
                NormalizeQuality(dto.Quality),
                string.IsNullOrWhiteSpace(dto.OutputDir) ? null : dto.OutputDir,
                dto.EarlierOutputDirs ?? Array.Empty<string>());
        }
        catch { return new SavedSettings(DurationCap.SixHours, 1080, null, Array.Empty<string>()); }
    }

    private sealed record DurationDto(int DurationHours);
    private sealed record SettingsDto(int DurationHours, int Quality, string? OutputDir, string[]? EarlierOutputDirs);
}

/// <summary>What the Windows app remembers: duration cap, 720 or 1080, and output folders.</summary>
public sealed record SavedSettings(DurationCap Duration, int Quality, string? OutputDir, IReadOnlyList<string> EarlierOutputDirs);
