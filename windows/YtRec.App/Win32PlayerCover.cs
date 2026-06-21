using System.Runtime.InteropServices;

namespace YtRec.App;

/// <summary>An opaque privacy "lid" that hides the live YouTube player from the user (Option C). It is a plain
/// Win32 popup (so it aligns 1:1 in physical pixels with the raw-Win32 <see cref="Win32PlayerHost"/> — no WinUI
/// DPI mismatch), filled with a solid brand-dark brush, positioned exactly over the player rect and inserted
/// **directly above the player in the z-order — NOT topmost**. That hides the player even on a bare
/// single-monitor desktop, yet stays low enough that it never covers the user's other windows (they sit above
/// it), preserving the "keep working while it records" UX. WGC captures the player's OWN surface, so the lid is
/// never in the recording.</summary>
public sealed class Win32PlayerCover
{
    public IntPtr Hwnd { get; private set; }

    /// <summary>Create the lid over (0,0,w,h) and slot it just above <paramref name="playerHwnd"/>.</summary>
    public void Show(IntPtr playerHwnd, int w, int h)
    {
        EnsureClass();
        Hwnd = CreateWindowEx(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, ClassName, "YT Rec 錄製中",
            WS_POPUP, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) return;
        ShowWindow(Hwnd, SW_SHOWNA);
        PlaceAbove(playerHwnd, w, h);
    }

    /// <summary>Keep the lid exactly over the player after a resize, and re-assert it sits just above the player.</summary>
    public void PlaceAbove(IntPtr playerHwnd, int w, int h)
    {
        if (Hwnd == IntPtr.Zero) return;
        SetWindowPos(Hwnd, playerHwnd, 0, 0, w, h, SWP_NOACTIVATE);
    }

    public void Close()
    {
        if (Hwnd != IntPtr.Zero) { DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
    }

    // ── Win32 interop ──
    private const string ClassName = "YtRecPlayerCoverWindow";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNA = 8;
    private const uint SWP_NOACTIVATE = 0x10;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate s_wndProc = (h, m, w, l) => DefWindowProc(h, m, w, l);
    private static bool s_registered;

    private static void EnsureClass()
    {
        if (s_registered) return;
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = CreateSolidBrush(0x00141212), // brand-dark #121214 as COLORREF (0x00BBGGRR)
        };
        RegisterClassEx(ref cls);
        s_registered = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
