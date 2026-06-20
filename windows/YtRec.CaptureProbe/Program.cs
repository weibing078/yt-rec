using YtRec.Capture;

// Usage:
//   ytrec-capture --list                       list visible windows
//   ytrec-capture "<title substr>" [seconds]   capture that window, report frame count

if (args.Length >= 1 && args[0] == "--list")
{
    foreach (var (h, t) in WindowFinder.ListVisible())
        Console.WriteLine($"{h.ToInt64():X8}  {t}");
    return 0;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: ytrec-capture --list | \"<title-substring>\" [seconds]");
    return 2;
}

var sub = args[0];
var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 3;

var hwnd = WindowFinder.FindByTitle(sub);
if (hwnd is null)
{
    Console.Error.WriteLine($"no visible window matching '{sub}'. Try --list.");
    return 3;
}

Console.WriteLine($"capturing hwnd 0x{hwnd.Value.ToInt64():X} for {seconds}s ...");
var r = await WindowCapture.CountFramesAsync(hwnd.Value, seconds);
Console.WriteLine($"supported={r.Supported} itemCreated={r.ItemCreated} frames={r.Frames} size={r.Width}x{r.Height}");
return r.Frames > 0 ? 0 : 1;
