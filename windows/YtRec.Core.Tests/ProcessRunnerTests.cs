using System.Diagnostics;
using YtRec.Core;

namespace YtRec.Core.Tests;

// Real subprocess tests; commands chosen per-OS so they run on both macOS and Windows.
public class ProcessRunnerTests
{
    private static (string exe, string[] args) Echo(string text) =>
        OperatingSystem.IsWindows() ? ("cmd.exe", new[] { "/c", "echo", text }) : ("/bin/echo", new[] { text });

    private static (string exe, string[] args) ExitOne() =>
        OperatingSystem.IsWindows() ? ("cmd.exe", new[] { "/c", "exit", "1" }) : ("/bin/sh", new[] { "-c", "exit 1" });

    private static (string exe, string[] args) SleepLong() =>
        OperatingSystem.IsWindows() ? ("cmd.exe", new[] { "/c", "ping", "127.0.0.1", "-n", "30" }) : ("/bin/sleep", new[] { "30" });

    [Fact]
    public async Task EchoesStdout()
    {
        var (exe, args) = Echo("hello world");
        var r = await new ProcessRunner().RunAsync(exe, args);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello world", r.Output);
        Assert.False(r.WasCancelled);
    }

    [Fact]
    public async Task NonZeroExitCode()
    {
        var (exe, args) = ExitOne();
        var r = await new ProcessRunner().RunAsync(exe, args);
        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task NonexistentExecutableResolvesWithoutHanging()
    {
        var r = await new ProcessRunner().RunAsync("definitely-not-a-real-binary-xyz", Array.Empty<string>());
        Assert.Equal(-1, r.ExitCode);
        Assert.False(r.WasCancelled);
    }

    [Fact]
    public async Task CancellationStopsTheProcessQuickly()
    {
        var (exe, args) = SleepLong();
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = new ProcessRunner().RunAsync(exe, args, ct: cts.Token);
        cts.CancelAfter(200);
        var r = await task;
        sw.Stop();
        Assert.True(r.WasCancelled);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"took {sw.Elapsed}");
    }
}
