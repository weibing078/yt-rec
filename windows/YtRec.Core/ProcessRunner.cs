using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace YtRec.Core;

public sealed record ProcessResult(int ExitCode, string Output, bool WasCancelled);

/// <summary>Abstraction over running a child process — lets the engine be unit-tested with a stub.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string? workingDir = null, Action<string>? onLine = null, CancellationToken ct = default);
}

/// <summary>Runs a child process: merged stdout+stderr split into lines (incl. \r progress updates),
/// keeps the last 200 lines, supports graceful cancel. Mirrors mac ProcessRunner.swift. Cross-platform.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string? workingDir = null, Action<string>? onLine = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;

        var collector = new LineCollector(onLine);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!proc.Start())
                return new ProcessResult(-1, "無法啟動", false);
        }
        catch (Exception e)
        {
            return new ProcessResult(-1, $"無法啟動：{e.Message}", false);
        }

        var cancelled = false;
        // Graceful interrupt, then hard kill after 5s if it ignores it.
        await using var reg = ct.Register(() =>
        {
            cancelled = true;
            Interrupt(proc);
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000).ConfigureAwait(false);
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            });
        });

        var pumpOut = PumpAsync(proc.StandardOutput, collector);
        var pumpErr = PumpAsync(proc.StandardError, collector);
        await Task.WhenAll(pumpOut, pumpErr).ConfigureAwait(false);
        await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        collector.Flush();

        return new ProcessResult(proc.ExitCode, collector.Tail(), cancelled);
    }

    private static async Task PumpAsync(StreamReader reader, LineCollector collector)
    {
        var buf = new char[4096];
        int n;
        while ((n = await reader.ReadAsync(buf).ConfigureAwait(false)) > 0)
            collector.Feed(buf.AsSpan(0, n));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private static void Interrupt(Process p)
    {
        try
        {
            if (p.HasExited) return;
            if (OperatingSystem.IsWindows())
                p.Kill(entireProcessTree: true); // no clean Ctrl+C to a child without a shared console
            else
                _ = kill(p.Id, 2); // SIGINT — lets yt-dlp finalize its .part
        }
        catch { /* best effort */ }
    }
}

/// <summary>Splits a byte/char stream into trimmed non-empty lines on \n or \r (yt-dlp progress uses \r),
/// keeps the last 200, and invokes a callback per line. Thread-safe.</summary>
public sealed class LineCollector
{
    private const int MaxTail = 200;
    private readonly Action<string>? _onLine;
    private readonly object _lock = new();
    private readonly StringBuilder _buf = new();
    private readonly LinkedList<string> _tail = new();

    public LineCollector(Action<string>? onLine) => _onLine = onLine;

    public void Feed(ReadOnlySpan<char> chunk)
    {
        List<string>? emitted = null;
        lock (_lock)
        {
            foreach (var ch in chunk)
            {
                if (ch is '\n' or '\r')
                {
                    var line = Take();
                    if (line != null) (emitted ??= new List<string>()).Add(line);
                }
                else _buf.Append(ch);
            }
        }
        if (emitted != null)
            foreach (var l in emitted) _onLine?.Invoke(l);
    }

    public void Feed(string chunk) => Feed(chunk.AsSpan());

    public void Flush()
    {
        string? line;
        lock (_lock) line = Take();
        if (line != null) _onLine?.Invoke(line);
    }

    public string Tail()
    {
        lock (_lock) return string.Join("\n", _tail);
    }

    // Caller holds _lock.
    private string? Take()
    {
        var line = _buf.ToString().Trim();
        _buf.Clear();
        if (line.Length == 0) return null;
        _tail.AddLast(line);
        while (_tail.Count > MaxTail) _tail.RemoveFirst();
        return line;
    }
}
