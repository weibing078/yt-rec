using System.Runtime.InteropServices;
using System.Text;

namespace YtRec.Capture;

/// <summary>Find top-level windows by title (for choosing a capture target).</summary>
public static class WindowFinder
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static IReadOnlyList<(IntPtr Hwnd, string Title)> ListVisible()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var len = GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            list.Add((h, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static IntPtr? FindByTitle(string substring)
    {
        foreach (var (h, t) in ListVisible())
            if (t.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return h;
        return null;
    }

    public static IntPtr? Foreground()
    {
        var h = GetForegroundWindow();
        return h == IntPtr.Zero ? null : h;
    }

    // ── Monitor + rect helpers (for monitor-capture + crop: WGC window-capture can't see a WebView2's
    //    video swapchain, so we capture the monitor and crop to the player window's rectangle). ──

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    /// <summary>The monitor a window is on (defaults to the primary).</summary>
    public static IntPtr MonitorForWindow(IntPtr hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY);

    /// <summary>A window's crop rectangle relative to <paramref name="hmonitor"/>'s top-left (X, Y, W, H),
    /// clamped to even dimensions for yuv420p.</summary>
    public static (int X, int Y, int W, int H) CropRect(IntPtr hwnd, IntPtr hmonitor)
    {
        GetWindowRect(hwnd, out var wr);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hmonitor, ref mi);
        var x = Math.Max(0, wr.Left - mi.rcMonitor.Left);
        var y = Math.Max(0, wr.Top - mi.rcMonitor.Top);
        var w = (wr.Right - wr.Left) & ~1;
        var h = (wr.Bottom - wr.Top) & ~1;
        return (x, y, w, h);
    }
}
