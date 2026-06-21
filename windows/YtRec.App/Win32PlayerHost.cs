using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using YtRec.Core;

namespace YtRec.App;

/// <summary>The player, hosted in a plain Win32 window (NOT the WinUI WebView2 control). The WinUI control is
/// visual-hosted (composition), so WGC never sees its video. A windowed WebView2
/// (<c>CreateCoreWebView2Controller(hwnd)</c>) renders into a real HWND that WGC captures directly. The window
/// stays on-screen at the top-left, sent to the bottom of the z-order and (Option C) covered by an opaque lid —
/// Windows yields no WGC frames for a fully off-screen window, so it must stay composited. The page CSS-fills the
/// player to the whole window, so the captured window IS the video (no crop); the host resizes the window to the
/// content-driven target aspect via <see cref="YtRec.Core.CaptureGeometry"/>.</summary>
public sealed class Win32PlayerHost
{
    /// <summary>Provisional creation size (landscape 1080p); resized to the content's target aspect once the
    /// source dimensions are known (<see cref="Resize"/>).</summary>
    public const int Width = 1920, Height = 1080;

    public IntPtr Hwnd { get; private set; }
    public uint BrowserProcessId { get; private set; }
    public event Action? Ended;

    /// <summary>The source video's pixel dimensions (videoWidth, videoHeight), reported by the page JS — the
    /// host derives landscape/portrait + the output size from this. Null until the player reports.</summary>
    public (int W, int H)? VideoDims { get; private set; }

    /// <summary>The inline video PICTURE rect as fractions of the window (x, y, w, h), reported by the page —
    /// the recorder crops to it so the output is the video only (no page chrome / no pillarbox). Null until
    /// laid out.</summary>
    public (double X, double Y, double W, double H)? VideoRectFrac { get; private set; }

    /// <summary>Drop the cached rect so a stale (pre-resize) value can't be used — the caller then waits for a
    /// fresh one after resizing the window.</summary>
    public void ClearVideoRect() => VideoRectFrac = null;

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task LoadAsync(string watchUrl, string userDataFolder)
    {
        EnsureClass();
        var hInst = GetModuleHandle(null);
        // On-screen (must be — Windows won't composite a fully off-screen window, so WGC gets no frames), but
        // background/coverable: NOT topmost, WS_EX_NOACTIVATE (no focus-steal), sent to the back. WGC
        // window-capture keeps capturing it while it's covered by the user's windows (verified), so the user
        // works over it (mac §4 occludable). Placed at top-left behind everything.
        Hwnd = CreateWindowEx(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, ClassName, "YT Rec Player",
            WS_POPUP, 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        ShowWindow(Hwnd, SW_SHOWNA);
        SetWindowPos(Hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = PlayerAssets.BrowserArguments };
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);
        var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)Hwnd);
        _controller = await env.CreateCoreWebView2ControllerAsync(windowRef);
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, Width, Height);
        _controller.IsVisible = true;

        _core = _controller.CoreWebView2;
        BrowserProcessId = _core.BrowserProcessId;
        _core.Settings.AreDefaultContextMenusEnabled = false;
        _core.Settings.IsStatusBarEnabled = false;

        _core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("state", out var s) && s.GetString() == "ended") Ended?.Invoke();
                if (root.TryGetProperty("dims", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() == 2)
                    VideoDims = (d[0].GetInt32(), d[1].GetInt32());
                if (root.TryGetProperty("rect", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() == 4)
                    VideoRectFrac = (r[0].GetDouble(), r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble());
            }
            catch { }
        };
        _core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            await _core.ExecuteScriptAsync(PlayerAssets.FillPlayAndReportScript);
            _ready.TrySetResult();
        };

        _core.Navigate(watchUrl);
        await _ready.Task;
    }

    /// <summary>Resize the host window + WebView2 to the content-driven capture size, kept at the top-left and
    /// the bottom of the z-order. Called once the source dimensions are known, before capture starts.</summary>
    public void Resize(int w, int h)
    {
        if (Hwnd == IntPtr.Zero) return;
        // Push the window 99.99% off the top-left, leaving only a 2 px sliver on-screen: WGC needs the window
        // composited (a *fully* off-screen window yields no frames), but it captures the window's whole backing
        // surface regardless of how much is visible — so the user effectively never sees it (the Mac
        // off-screen experience) and no opaque lid is needed. Falls back gracefully if WGC clips (we verify).
        SetWindowPos(Hwnd, HWND_BOTTOM, 2 - w, 2 - h, w, h, SWP_NOACTIVATE);
        if (_controller is not null) _controller.Bounds = new Windows.Foundation.Rect(0, 0, w, h);
    }

    /// <summary>Primary monitor size in physical pixels. The process is PerMonitorV2, so GetSystemMetrics
    /// returns real device pixels — used to fit the capture window within the screen.</summary>
    public static (int W, int H) PrimaryScreenPixels()
        => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    public async Task SeekAsync(double seconds)
    {
        if (_core is not null) await _core.ExecuteScriptAsync(PlayerAssets.SeekScript(seconds));
    }

    public async Task<string?> ProgressStateAsync()
        => _core is null ? null : await _core.ExecuteScriptAsync(PlayerAssets.ProgressStateScript);

    public void Close()
    {
        try { _controller?.Close(); } catch { }
        _controller = null; _core = null;
        if (Hwnd != IntPtr.Zero) { DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
    }

    // ── Win32 interop ──
    private const string ClassName = "YtRecPlayerHostWindow";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNA = 8;
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

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
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
