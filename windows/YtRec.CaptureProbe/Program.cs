using YtRec.Capture;

// Usage:
//   ytrec-capture --list                       list visible windows
//   ytrec-capture --auto [seconds]             auto-pick a window that captures; report it
//   ytrec-capture --foreground [seconds]       capture the current foreground window
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

    default:
    {
        var hwnd = WindowFinder.FindByTitle(args[0]);
        if (hwnd is null) { Console.Error.WriteLine($"no visible window matching '{args[0]}'. Try --list."); return 3; }
        return await Report($"window '{args[0]}'", hwnd.Value, seconds);
    }
}
