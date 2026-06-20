using System.Runtime.InteropServices;
using System.Text;

namespace YtRec.Capture;

/// <summary>Find top-level windows by title (for choosing a capture target).</summary>
public static class WindowFinder
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
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
}
