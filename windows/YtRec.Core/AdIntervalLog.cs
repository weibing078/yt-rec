namespace YtRec.Core;

/// <summary>One mid-roll ad on the finished file's timeline (seconds from the first written frame).</summary>
public readonly record struct AdSpan(int StartSec, int EndSec);

/// <summary>Samples the ad flag once a second and renders <c>&lt;mp4-without-extension&gt;.廣告時段.txt</c>.
/// Zero spans render as null so the caller writes no file. Crash recovery must not call this.</summary>
public sealed class AdIntervalLog
{
    private int? _openStart;
    private readonly List<AdSpan> _closed = new();

    public void Observe(bool ad, int fileSec)
    {
        if (fileSec < 0) fileSec = 0;
        if (ad)
        {
            _openStart ??= fileSec;
            return;
        }
        if (_openStart is int start) Close(start, fileSec);
    }

    public void CloseOpen(int fileSec)
    {
        if (fileSec < 0) fileSec = 0;
        if (_openStart is int start) Close(start, fileSec);
    }

    public IReadOnlyList<AdSpan> Spans => _closed;

    public string? Render() => Render(_closed);

    public static string? Render(IReadOnlyList<AdSpan> spans)
    {
        if (spans.Count == 0) return null;
        var lines = new string[spans.Count + 1];
        lines[0] = "每秒偵測，誤差約 ±1-2 秒";
        for (int i = 0; i < spans.Count; i++)
            lines[i + 1] = Clock(spans[i].StartSec) + "–" + Clock(spans[i].EndSec);
        return string.Join('\n', lines) + "\n";
    }

    public static string Clock(int sec)
    {
        if (sec < 0) sec = 0;
        return $"{sec / 3600:00}:{(sec % 3600) / 60:00}:{sec % 60:00}";
    }

    public static string SidecarPath(string mp4Path)
    {
        var dir = Path.GetDirectoryName(mp4Path) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(mp4Path) + ".廣告時段.txt");
    }

    private void Close(int start, int end)
    {
        _openStart = null;
        if (end > start) _closed.Add(new AdSpan(start, end));
    }
}
