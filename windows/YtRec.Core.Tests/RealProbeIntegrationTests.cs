using YtRec.Core;

namespace YtRec.Core.Tests;

// L3 integration: drives the REAL bundled yt-dlp against YouTube. Gated (needs network + the binary):
//   YTREC_RUN_NETWORK_TESTS=1 YTREC_YTDLP=/abs/path/to/yt-dlp dotnet test
// Mirrors the macOS LCF_RUN_NETWORK_TESTS gate. Skipped (no-op) otherwise so the suite stays green offline.
public class RealProbeIntegrationTests
{
    [Fact]
    public async Task RealProbe_MeAtTheZoo_ParsesNotLive()
    {
        if (Environment.GetEnvironmentVariable("YTREC_RUN_NETWORK_TESTS") != "1") return;
        var ytdlp = Environment.GetEnvironmentVariable("YTREC_YTDLP");
        Assert.False(string.IsNullOrEmpty(ytdlp), "set YTREC_YTDLP to the yt-dlp binary path");

        var dir = Path.Combine(Path.GetTempPath(), "ytrec-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProbeInfo? captured = null;
            using var cts = new CancellationTokenSource();
            var engine = new YtDlpEngine(ytdlp!, null, () => new ProcessRunner())
            {
                OnProbe = p => { captured = p; cts.Cancel(); }, // stop before it actually downloads
            };

            await engine.StartAsync("https://youtu.be/jNQXAC9IVRw", dir, 1080, ct: cts.Token);

            Assert.NotNull(captured);
            Assert.Equal("jNQXAC9IVRw", captured!.Id);
            Assert.Equal("not_live", captured.LiveStatus);
        }
        finally { Directory.Delete(dir, true); }
    }
}
