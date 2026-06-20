using YtRec.Core;

// Headless runner for the download track. Usage:
//   ytrec <youtube-url> [outputDir] [--section <from> <to>] [--max-height <n>]
// Binaries (yt-dlp/ffmpeg) are located via BinaryLocator (vendor/bin, YTREC_BIN_DIR, or PATH).

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("usage: ytrec <youtube-url> [outputDir] [--section <from> <to>] [--max-height <n>]");
    return 2;
}

var url = args[0];
var positional = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToArray();
var outDir = positional.Length > 0 ? positional[0] : Path.Combine(Path.GetTempPath(), "ytrec-cli");
Directory.CreateDirectory(outDir);

int maxHeight = 1080;
string? section = null;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--max-height" && i + 1 < args.Length) maxHeight = int.Parse(args[++i]);
    else if (args[i] == "--section" && i + 2 < args.Length)
        section = Timecode.Section(args[i + 1], args[i + 2])?.Arg;
}

if (YtUrl.VideoId(url) is null) { Console.Error.WriteLine($"not a YouTube URL: {url}"); return 2; }

var ytdlp = BinaryLocator.Resolve(BinaryLocator.Tool.YtDlp);
var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg);
if (ytdlp is null)
{
    Console.Error.WriteLine("yt-dlp not found. Set YTREC_BIN_DIR or place it in vendor/bin.");
    foreach (var c in BinaryLocator.Candidates(BinaryLocator.Tool.YtDlp)) Console.Error.WriteLine($"  tried: {c}");
    return 3;
}
Console.WriteLine($"yt-dlp : {ytdlp}");
Console.WriteLine($"ffmpeg : {ffmpeg ?? "(none — yt-dlp may not be able to merge)"}");
Console.WriteLine($"output : {outDir}");
Console.WriteLine(section is null ? "mode   : full" : $"mode   : section {section}");

var engine = new YtDlpEngine(ytdlp, ffmpeg is null ? null : Path.GetDirectoryName(ffmpeg), () => new ProcessRunner());
engine.OnProbe = p => Console.WriteLine($"[probe ] {p.Id} | {p.Title} | live={p.LiveStatus}");
engine.OnStatus = s => Console.WriteLine($"[status] {s}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var outcome = await engine.StartAsync(url, outDir, maxHeight, section: section, ct: cts.Token);

switch (outcome)
{
    case DownloadOutcome.Success ok:
        Console.WriteLine($"[done  ] SUCCESS → {ok.File}");
        return 0;
    case DownloadOutcome.TerminalFailure f:
        Console.WriteLine($"[done  ] FAILED: {f.Reason}");
        return 1;
    default:
        Console.WriteLine($"[done  ] {outcome.GetType().Name}");
        return 1;
}
