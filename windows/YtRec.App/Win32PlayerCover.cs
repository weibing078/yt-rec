using System.Runtime.InteropServices;

namespace YtRec.App;

/// <summary>An opaque privacy "lid" that hides the live YouTube player from the user (Option C). It is a plain
/// Win32 popup (so it aligns 1:1 in physical pixels with the raw-Win32 <see cref="Win32PlayerHost"/> — no WinUI
/// DPI mismatch), filled with a solid brand-dark brush + a centered "錄製中" label (so it reads as intentional,
/// not a frozen black window), positioned exactly over the player rect and inserted **directly above the player
/// in the z-order — NOT topmost**. That hides the player even on a bare single-monitor desktop, yet stays low
/// enough that it never covers the user's other windows (they sit above it), preserving the "keep working while
/// it records" UX. WGC captures the player's OWN surface, so the lid is never in the recording.</summary>
public sealed class Win32PlayerCover
{
    public IntPtr Hwnd { get; private set; }
    private IntPtr _label;

    /// <summary>Create the lid over (0,0,w,h) and slot it just above <paramref name="playerHwnd"/>.</summary>
    public void Show(IntPtr playerHwnd, int w, int h)
    {
        EnsureClass();
        var hInst = GetModuleHandle(null);
        Hwnd = CreateWindowEx(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, ClassName, "YT Rec 錄製中",
            WS_POPUP, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) return;
        // Centered caption so the cover looks intentional. A STATIC child degrades gracefully (no label if it
        // fails) — never a crash. Text colour comes from WM_CTLCOLORSTATIC.
        _label = CreateWindowEx(0, "STATIC", "●  錄製中　·　YT Rec 正在側錄此畫面",
            WS_CHILD | WS_VISIBLE | SS_CENTER, 0, h / 2 - 30, w, 60, Hwnd, IntPtr.Zero, hInst, IntPtr.Zero);
        if (_label != IntPtr.Zero && s_font != IntPtr.Zero) SendMessage(_label, WM_SETFONT, s_font, (IntPtr)1);
        ShowWindow(Hwnd, SW_SHOWNA);
        PlaceAbove(playerHwnd, w, h);
    }

    /// <summary>Keep the lid exactly over the player and sitting DIRECTLY ABOVE it in the z-order. Win32
    /// "insert after" puts a window *behind* the reference, so `SetWindowPos(lid, player)` actually hid the lid
    /// behind the player (the live page showed). Correct order: send the lid to the bottom (sized), then move
    /// the player just behind the lid — the pair stays at the bottom of the z-order (the user's other windows
    /// remain on top; on a bare desktop the opaque lid covers the live player).</summary>
    public void PlaceAbove(IntPtr playerHwnd, int w, int h)
    {
        if (Hwnd == IntPtr.Zero) return;
        SetWindowPos(Hwnd, HWND_BOTTOM, 0, 0, w, h, SWP_NOACTIVATE);
        SetWindowPos(playerHwnd, Hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Close()
    {
        if (Hwnd != IntPtr.Zero) { DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
    }

    // ── Win32 interop ──
    private const string ClassName = "YtRecPlayerCoverWindow";
    private const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SS_CENTER = 0x1;
    private const int SW_SHOWNA = 8;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint WM_SETFONT = 0x0030, WM_CTLCOLORSTATIC = 0x0138;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate s_wndProc = WndProc;
    private static bool s_registered;
    private static IntPtr s_brush, s_font;

    private static IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        if (m == WM_CTLCOLORSTATIC)
        {
            SetTextColor(w, 0x00C8C8C8);   // light grey text (COLORREF 0x00BBGGRR)
            SetBkColor(w, 0x00141212);     // match the dark lid so the label blends
            return s_brush;                // brush painted behind the static's text
        }
        return DefWindowProc(h, m, w, l);
    }

    private static void EnsureClass()
    {
        if (s_registered) return;
        s_brush = CreateSolidBrush(0x00141212); // brand-dark #121214 as COLORREF (0x00BBGGRR)
        s_font = CreateFontW(36, 0, 0, 0, 600, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 0, 0, "Microsoft JhengHei UI");
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = s_brush,
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEsc, int cOrient, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrec, uint iClipPrec,
        uint iQuality, uint iPitchAndFamily, string pszFaceName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
