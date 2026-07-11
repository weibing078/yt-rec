namespace YtRec.Core;

/// <summary>A monitor's bounding rectangle in the virtual-desktop coordinate space (the same space
/// SetWindowPos uses). Right/Bottom are exclusive, matching Win32 RECT semantics.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Pure logic for parking the off-screen player window (mirrors the invariant established in a36fbf7):
/// the window's ORIGIN must sit inside a real monitor (Windows clamps a window whose origin is fully off-screen
/// back into view), while the window BODY hangs off the virtual desktop's bottom-right so the user sees at most a
/// 2 px sliver. Choosing the right monitor keeps the body from landing on a second monitor and re-parking on a
/// display change keeps a mid-recording resolution shrink from pushing the whole window off every screen.</summary>
public static class OffscreenPark
{
    /// <summary>Origin (top-left) for the park: 2 px inside the bottom-right corner of the monitor whose bottom
    /// edge is lowest (ties broken by the rightmost). That monitor is the most bottom-right one, so the body,
    /// hanging down-and-right from the origin, spills into empty virtual space rather than onto another monitor.
    /// Empty list → (0, 0) fallback (caller should never hit this on a real display).</summary>
    public static (int X, int Y) Origin(IReadOnlyList<ScreenRect> monitors)
    {
        if (monitors.Count == 0) return (0, 0);
        var best = monitors[0];
        foreach (var m in monitors)
            if (m.Bottom > best.Bottom || (m.Bottom == best.Bottom && m.Right > best.Right))
                best = m;
        return (best.Right - 2, best.Bottom - 2);
    }
}
