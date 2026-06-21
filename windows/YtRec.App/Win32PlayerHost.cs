using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using YtRec.Core;

namespace YtRec.App;

/// <summary>The off-screen-style player, hosted in a plain Win32 window (NOT the WinUI WebView2 control).
/// The WinUI control is visual-hosted (composition), so WGC never sees its video. A windowed WebView2
/// (<c>CreateCoreWebView2Controller(hwnd)</c>) renders into a real HWND that WGC can capture. The window is
/// topmost/visible at the top-left; the recorder captures its monitor and crops to this rectangle.</summary>
public sealed class Win32PlayerHost
{
    public const int Width = 1280, Height = 720;

    public IntPtr Hwnd { get; private set; }
    public uint BrowserProcessId { get; private set; }
    public event Action? Ended;

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task LoadAsync(string watchUrl, string userDataFolder)
    {
        EnsureClass();
        var hInst = GetModuleHandle(null);
        Hwnd = CreateWindowEx(WS_EX_TOPMOST, ClassName, "YT Rec Player",
            WS_POPUP | WS_VISIBLE, 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        ShowWindow(Hwnd, SW_SHOW);
        SetForegroundWindow(Hwnd);

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
                if (doc.RootElement.TryGetProperty("state", out var s) && s.GetString() == "ended") Ended?.Invoke();
            }
            catch { }
        };
        _core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            await _core.ExecuteScriptAsync(PlayerAssets.ForcePlayAndReportScript);
            _ready.TrySetResult();
        };

        _core.Navigate(watchUrl);
        await _ready.Task;
    }

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
    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_EX_TOPMOST = 0x00000008;
    private const int SW_SHOW = 5;

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
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
