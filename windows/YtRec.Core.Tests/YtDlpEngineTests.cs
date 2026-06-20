using YtRec.Core;

namespace YtRec.Core.Tests;

public class YtDlpEngineTests
{
    // Stub runner: returns scripted results based on the args of each invocation.
    private sealed class StubRunner : IProcessRunner
    {
        private readonly Func<IReadOnlyList<string>, ProcessResult> _handler;
        public StubRunner(Func<IReadOnlyList<string>, ProcessResult> handler) => _handler = handler;
        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            string? workingDir = null, Action<string>? onLine = null, CancellationToken ct = default)
            => Task.FromResult(_handler(arguments));
    }

    private static Func<IProcessRunner> Factory(Func<IReadOnlyList<string>, ProcessResult> handler)
        => () => new StubRunner(handler);

    private static bool IsProbe(IReadOnlyList<string> args) => args.Contains("--print");

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ytrec-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteDummyVideo(string dir, string name = "video.mp4")
        => File.WriteAllBytes(Path.Combine(dir, name), new byte[200_000]);

    private static ProcessResult Probe(string line) => new(0, line, false);

    [Fact]
    public async Task EndedVod_DownloadsSuccessfully()
    {
        var dir = TempDir();
        try
        {
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
            {
                if (IsProbe(args)) return Probe("jNQXAC9IVRw\tMe at the zoo\tnot_live\tNA");
                WriteDummyVideo(dir);
                return new ProcessResult(0, "", false);
            }));
            var outcome = await engine.StartAsync("https://youtu.be/jNQXAC9IVRw", dir, 1080);
            var success = Assert.IsType<DownloadOutcome.Success>(outcome);
            Assert.EndsWith("video.mp4", success.File);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task PrivateVideoProbe_IsTerminalFailure()
    {
        var dir = TempDir();
        try
        {
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
                IsProbe(args)
                    ? new ProcessResult(1, "ERROR: Private video. Sign in if you've been granted access", false)
                    : new ProcessResult(0, "", false)));
            var outcome = await engine.StartAsync("https://youtu.be/private", dir, 1080);
            var fail = Assert.IsType<DownloadOutcome.TerminalFailure>(outcome);
            Assert.Equal("影片已被設為私人／隱藏", fail.Reason);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task AutoMode_InProgressLive_IsSkipped()
    {
        var dir = TempDir();
        try
        {
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
                IsProbe(args) ? Probe("abcDEF12345\tLive Now\tis_live\tNA") : new ProcessResult(0, "", false)));
            var outcome = await engine.StartAsync("https://youtu.be/live", dir, 1080, autoMode: true);
            Assert.IsType<DownloadOutcome.SkippedAutoLive>(outcome);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LongRunningLive_IsMarathon()
    {
        var dir = TempDir();
        try
        {
            var started = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5 * 3600; // 5h ago
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
                IsProbe(args) ? Probe($"abcDEF12345\t24h Channel\tis_live\t{started}") : new ProcessResult(0, "", false)));
            var outcome = await engine.StartAsync("https://youtu.be/marathon", dir, 1080);
            Assert.IsType<DownloadOutcome.Marathon>(outcome);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RetryableFirstStrategy_ThenSucceeds_WithinRound()
    {
        var dir = TempDir();
        try
        {
            var downloadAttempts = 0;
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
            {
                if (IsProbe(args)) return Probe("abcDEF12345\tEnded\tnot_live\tNA");
                downloadAttempts++;
                if (downloadAttempts == 1) return new ProcessResult(1, "ERROR: HTTP Error 503", false); // retryable
                WriteDummyVideo(dir);
                return new ProcessResult(0, "", false);
            }));
            var outcome = await engine.StartAsync("https://youtu.be/x", dir, 1080, pollSeconds: 0);
            Assert.IsType<DownloadOutcome.Success>(outcome);
            Assert.True(downloadAttempts >= 2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task SectionDownload_UsesDownloadSections()
    {
        var dir = TempDir();
        try
        {
            var sawSectionFlag = false;
            var engine = new YtDlpEngine("yt-dlp", null, Factory(args =>
            {
                if (IsProbe(args)) return Probe("abcDEF12345\tEnded\tnot_live\tNA");
                if (args.Contains("--download-sections")) sawSectionFlag = true;
                WriteDummyVideo(dir);
                return new ProcessResult(0, "", false);
            }));
            var outcome = await engine.StartAsync("https://youtu.be/x", dir, 1080, section: "*00:00:05-00:00:12");
            Assert.IsType<DownloadOutcome.Success>(outcome);
            Assert.True(sawSectionFlag);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CancelledToken_ReturnsCancelled()
    {
        var dir = TempDir();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var engine = new YtDlpEngine("yt-dlp", null, Factory(_ => new ProcessResult(0, "", false)));
            var outcome = await engine.StartAsync("https://youtu.be/x", dir, 1080, ct: cts.Token);
            Assert.IsType<DownloadOutcome.Cancelled>(outcome);
        }
        finally { Directory.Delete(dir, true); }
    }
}
