using YtRec.Core;

namespace YtRec.Core.Tests;

public class BinaryLocatorTests
{
    [Fact]
    public void ExecutableName_HasExeOnWindowsOnly()
    {
        var expected = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        Assert.Equal(expected, BinaryLocator.ExecutableName(BinaryLocator.Tool.YtDlp));
    }

    [Fact]
    public void FirstExecutable_PrefersEarlierCandidate()
    {
        var candidates = new[] { "/a/yt-dlp", "/b/yt-dlp", "/c/yt-dlp" };
        // only /b and /c "exist" → first match is /b
        var found = BinaryLocator.FirstExecutable(candidates, p => p is "/b/yt-dlp" or "/c/yt-dlp");
        Assert.Equal("/b/yt-dlp", found);
    }

    [Fact]
    public void FirstExecutable_NoneExist_ReturnsNull()
    {
        var candidates = new[] { "/a/yt-dlp", "/b/yt-dlp" };
        Assert.Null(BinaryLocator.FirstExecutable(candidates, _ => false));
    }

    [Fact]
    public void Candidates_OrderIsBundledThenAppThenEnvThenPath()
    {
        var sep = Path.PathSeparator;
        var c = BinaryLocator.Candidates(BinaryLocator.Tool.Ffmpeg,
            baseDir: "/app", binDirEnv: "/override", pathEnv: $"/usr/bin{sep}/bin");
        var name = BinaryLocator.ExecutableName(BinaryLocator.Tool.Ffmpeg);
        Assert.Equal(Path.Combine("/app", "vendor", "bin", name), c[0]);
        Assert.Equal(Path.Combine("/app", name), c[1]);
        Assert.Equal(Path.Combine("/override", name), c[2]);
        Assert.Equal(Path.Combine("/usr/bin", name), c[3]);
        Assert.Equal(Path.Combine("/bin", name), c[4]);
    }

    [Fact]
    public void Candidates_OmitsEnvAndPathWhenEmpty()
    {
        var c = BinaryLocator.Candidates(BinaryLocator.Tool.YtDlp, baseDir: "/app", binDirEnv: "", pathEnv: "");
        Assert.Equal(2, c.Count); // only bundled + alongside-exe
    }
}
