using YtRec.Capture;
using YtRec.Core;

// Usage:
//   ytrec-capture --list                       list visible windows
//   ytrec-capture --auto [seconds]             auto-pick a window that captures; report it
//   ytrec-capture --foreground [seconds]       capture the current foreground window
//   ytrec-capture --record <secs> <out.mp4> [fps]  record foreground window → fragmented MP4
//   ytrec-capture "<title substr>" [seconds]   capture a window by title

static async Task<int> Report(string label, IntPtr hwnd, int seconds)
{
    Console.WriteLine($"{label}: hwnd 0x{hwnd.ToInt64():X}");
    var r = await WindowCapture.CountFramesAsync(hwnd, seconds);
    Console.WriteLine($"supported={r.Supported} itemCreated={r.ItemCreated} frames={r.Frames} size={r.Width}x{r.Height}");
    return r.Frames > 0 ? 0 : 1;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: ytrec-capture --list | --auto [s] | --foreground [s] | \"<title>\" [s]");
    return 2;
}

var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 3;

switch (args[0])
{
    case "--list":
        foreach (var (h, t) in WindowFinder.ListVisible())
            Console.WriteLine($"{h.ToInt64():X8}  {t}");
        return 0;

    case "--auto":
    {
        // Try visible windows (up to 12) until one yields frames — robust headless check.
        foreach (var (h, t) in WindowFinder.ListVisible().Take(12))
        {
            var r = await WindowCapture.CountFramesAsync(h, Math.Max(1, seconds));
            if (r.Frames > 0)
            {
                Console.WriteLine($"OK window='{t}' frames={r.Frames} size={r.Width}x{r.Height}");
                return 0;
            }
        }
        Console.WriteLine("no visible window produced frames (desktop locked, or no rendering windows?)");
        return 1;
    }

    case "--foreground":
    {
        var hwnd = WindowFinder.Foreground();
        if (hwnd is null) { Console.Error.WriteLine("no foreground window"); return 3; }
        return await Report("foreground", hwnd.Value, seconds);
    }

    case "--frames":
    {
        // --frames <count> <outdir>  : capture the foreground window as a PNG sequence
        if (args.Length < 3) { Console.Error.WriteLine("usage: --frames <count> <outdir>"); return 2; }
        var count = int.TryParse(args[1], out var c) ? c : 40;
        var dir = args[2];
        var fg = WindowFinder.Foreground();
        if (fg is null) { Console.Error.WriteLine("no foreground window"); return 3; }
        var saved = await WindowCapture.CaptureFramesToDirAsync(fg.Value, dir, count, maxSeconds: 8);
        Console.WriteLine($"saved {saved} frames to {dir}");
        return saved > 0 ? 0 : 1;
    }

    case "--record":
    {
        // --record <seconds> <out.mp4> [fps] [titleSubstr] : continuous-record a window to a fragmented MP4.
        // With a title substring, capture that window; otherwise the foreground window.
        if (args.Length < 3) { Console.Error.WriteLine("usage: --record <seconds> <out.mp4> [fps] [titleSubstr]"); return 2; }
        var secs = int.TryParse(args[1], out var sv) ? sv : 5;
        var outPath = args[2];
        var fps = args.Length > 3 && int.TryParse(args[3], out var fv) ? fv : 30;

        var ffmpeg = BinaryLocator.Resolve(BinaryLocator.Tool.Ffmpeg);
        if (ffmpeg is null) { Console.Error.WriteLine("ffmpeg not found (set YTREC_BIN_DIR or PATH)"); return 4; }

        var target = args.Length > 4 ? WindowFinder.FindByTitle(args[4]) : WindowFinder.Foreground();
        if (target is null) { Console.Error.WriteLine("no capture target window"); return 3; }

        var r = await ContinuousRecorder.RecordAsync(target.Value, ffmpeg, outPath, fps, secs);
        Console.WriteLine($"supported={r.Supported} size={r.Width}x{r.Height} frames={r.FramesWritten} " +
                          $"bytes={r.BytesWritten} ffmpegExit={r.FfmpegExitCode} -> {outPath}");
        if (r.Error is not null) Console.Error.WriteLine("error: " + r.Error);
        return r.FramesWritten > 0 && r.FfmpegExitCode == 0 ? 0 : 1;
    }

    case "--audio":
    {
        // --audio sys <seconds> <out.pcm>          system loopback (all audio)
        // --audio proc <pid> <seconds> <out.pcm>   process-loopback for a PID's tree (Win11 isolation)
        if (args.Length < 4) { Console.Error.WriteLine("usage: --audio sys <s> <out.pcm> | --audio proc <pid> <s> <out.pcm>"); return 2; }
        var mode = args[1];
        int secs; string outPath; uint pid = 0;
        if (mode == "sys") { secs = int.Parse(args[2]); outPath = args[3]; }
        else if (mode == "proc") { pid = uint.Parse(args[2]); secs = int.Parse(args[3]); outPath = args[4]; }
        else { Console.Error.WriteLine("mode must be sys|proc"); return 2; }

        var source = mode == "sys" ? AudioLoopbackCapture.Source.SystemLoopback : AudioLoopbackCapture.Source.ProcessLoopback;
        var cap = new AudioLoopbackCapture(source, pid);

        long bytes = 0; int peak = 0;
        await using var fs = File.Create(outPath);
        try
        {
            cap.Start((buf, count, _) =>
            {
                fs.Write(buf, 0, count);
                bytes += count;
                for (int i = 0; i + 1 < count; i += 2)
                {
                    int s = (short)(buf[i] | (buf[i + 1] << 8));
                    int a = Math.Abs(s);
                    if (a > peak) peak = a;
                }
            });
        }
        catch (Exception e) { Console.Error.WriteLine("acquire/start failed: " + e.Message); return 4; }
        await Task.Delay(secs * 1000);
        cap.Stop();
        var f = cap.Format;
        cap.Dispose();
        await fs.FlushAsync();
        Console.WriteLine($"mode={mode} pid={pid} bytes={bytes} peak={peak} fmt={f.SampleRate}/{f.Channels}/{f.BitsPerSample} -> {outPath}");
        return bytes > 0 ? 0 : 1;
    }

    case "--png":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: --png <out.png>"); return 2; }
        var outPath = args[1];
        // Foreground window first — most likely actively rendering and not occluded by us.
        var fg = WindowFinder.Foreground();
        if (fg is not null)
        {
            var (ok0, w0, h0, err0) = await WindowCapture.CaptureFrameToPngAsync(fg.Value, outPath, 5000);
            if (ok0) { Console.WriteLine($"OK foreground size={w0}x{h0} -> {outPath}"); return 0; }
            Console.WriteLine($"foreground failed: {err0}");
        }
        // Otherwise scan visible windows (skip our own console) to PNG.
        foreach (var (h, t) in WindowFinder.ListVisible().Take(15))
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (t.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Contains("conhost", StringComparison.OrdinalIgnoreCase)) continue;
            var (ok, w, hh, err) = await WindowCapture.CaptureFrameToPngAsync(h, outPath, 3000);
            if (ok) { Console.WriteLine($"OK window='{t}' size={w}x{hh} -> {outPath}"); return 0; }
            Console.WriteLine($"skip '{t}': {err}");
        }
        Console.WriteLine("no window captured to PNG");
        return 1;
    }

    default:
    {
        var hwnd = WindowFinder.FindByTitle(args[0]);
        if (hwnd is null) { Console.Error.WriteLine($"no visible window matching '{args[0]}'. Try --list."); return 3; }
        return await Report($"window '{args[0]}'", hwnd.Value, seconds);
    }
}
