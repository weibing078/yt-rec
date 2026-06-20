namespace YtRec.Core;

/// <summary>Locates the yt-dlp / ffmpeg executables. Mirrors mac BinaryLocator.swift.
/// Search order: bundled (app dir/vendor/bin) → YTREC_BIN_DIR env → PATH. Cross-platform
/// (adds .exe on Windows). The pure helpers are unit-tested without touching the real FS.</summary>
public static class BinaryLocator
{
    public enum Tool { YtDlp, Ffmpeg }

    /// <summary>Platform executable filename for a tool (yt-dlp / yt-dlp.exe).</summary>
    public static string ExecutableName(Tool tool)
    {
        var stem = tool == Tool.YtDlp ? "yt-dlp" : "ffmpeg";
        return OperatingSystem.IsWindows() ? stem + ".exe" : stem;
    }

    /// <summary>Ordered candidate paths for a tool. baseDir defaults to the app's base directory.</summary>
    public static IReadOnlyList<string> Candidates(Tool tool, string? baseDir = null,
        string? binDirEnv = null, string? pathEnv = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        binDirEnv ??= Environment.GetEnvironmentVariable("YTREC_BIN_DIR");
        pathEnv ??= Environment.GetEnvironmentVariable("PATH");
        var name = ExecutableName(tool);

        var list = new List<string>
        {
            Path.Combine(baseDir, "vendor", "bin", name), // bundled next to the app
            Path.Combine(baseDir, name),                  // alongside the exe
        };
        if (!string.IsNullOrEmpty(binDirEnv))
            list.Add(Path.Combine(binDirEnv, name));       // dev/override
        if (!string.IsNullOrEmpty(pathEnv))
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                list.Add(Path.Combine(dir, name));         // system PATH
        return list;
    }

    /// <summary>First candidate the predicate accepts (pure; inject the FS check for tests).</summary>
    public static string? FirstExecutable(IEnumerable<string> candidates, Func<string, bool> exists)
        => candidates.FirstOrDefault(exists);

    /// <summary>Resolve a tool's path against the real filesystem, or null if not found.</summary>
    public static string? Resolve(Tool tool, string? baseDir = null)
        => FirstExecutable(Candidates(tool, baseDir), File.Exists);

    /// <summary>Names of tools that could not be located (stable order: yt-dlp, ffmpeg).</summary>
    public static IReadOnlyList<string> MissingTools(string? baseDir = null)
    {
        var missing = new List<string>();
        if (Resolve(Tool.YtDlp, baseDir) is null) missing.Add(ExecutableName(Tool.YtDlp));
        if (Resolve(Tool.Ffmpeg, baseDir) is null) missing.Add(ExecutableName(Tool.Ffmpeg));
        return missing;
    }
}
