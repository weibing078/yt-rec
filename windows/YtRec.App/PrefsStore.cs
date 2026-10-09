using YtRec.Core;

namespace YtRec.App;

/// <summary>Local duration, quality, output folder, and recent files. Settings live in the user profile,
/// not inside the video folder, so changing the output folder does not hide them.</summary>
static class PrefsStore
{
    private static string Dir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT-Rec");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static SavedSettings LoadSettings()
    {
        try { return LocalPrefs.ReadSettings(File.ReadAllText(Path.Combine(Dir, "settings.json"))); }
        catch { return new SavedSettings(DurationCap.SixHours, 1080, null, Array.Empty<string>()); }
    }

    public static void SaveSettings(SavedSettings settings)
    {
        try { File.WriteAllText(Path.Combine(Dir, "settings.json"), LocalPrefs.WriteSettings(settings)); }
        catch { /* a prefs failure must not block recording */ }
    }

    public static IReadOnlyList<StoredRecent> LoadRecent()
    {
        try { return LocalPrefs.ReadRecent(File.ReadAllText(Path.Combine(Dir, "history.json")), File.Exists); }
        catch { return Array.Empty<StoredRecent>(); }
    }

    public static void SaveRecent(IReadOnlyList<StoredRecent> items)
    {
        try { File.WriteAllText(Path.Combine(Dir, "history.json"), LocalPrefs.WriteRecent(items)); }
        catch { }
    }
}
