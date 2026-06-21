using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using WinRT.Interop;
using YtRec.Core;

namespace YtRec.App;

/// <summary>The off-screen WebView2 player. Plays the YouTube stream at full resolution with the
/// anti-occlusion Chromium flags so frames keep flowing and audio keeps decoding while the window is
/// off-screen/occluded (mac §4 + §2d). Its own user-data-folder gives it a private process tree, which is
/// what makes Win11 per-process audio loopback isolate this stream's audio. Exposes the HWND (for WGC) and
/// the browser PID (for process-loopback audio).</summary>
public sealed partial class PlayerWindow : Window
{
    public IntPtr Hwnd { get; }
    public uint BrowserProcessId { get; private set; }

    /// <summary>Raised when the player reports the stream has ended (video 'ended' or player state ENDED).</summary>
    public event Action? Ended;

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PlayerWindow()
    {
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);
        Title = "YT Rec Player";

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsAlwaysOnTop = true; // must stay visible/unoccluded — WGC only updates a foreground/visible window
        }
        AppWindow.Resize(new SizeInt32(1280, 720));
        // Must be ON-screen (a monitor area) so DWM composites it — WGC returns no frames for a window
        // positioned entirely outside all monitors (verified). It may be occluded/covered (WGC still captures
        // real content), just not minimized and not off-screen. The floating monitor window sits in front.
        AppWindow.Move(new PointInt32(0, 0));
    }

    /// <summary>Create the environment (anti-occlusion flags + private user-data folder), navigate, and
    /// resolve once the player has loaded and the force-play script is injected.</summary>
    public async Task LoadAsync(string watchUrl, string userDataFolder)
    {
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = PlayerAssets.BrowserArguments };
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);
        await Web.EnsureCoreWebView2Async(env);

        var core = Web.CoreWebView2;
        BrowserProcessId = core.BrowserProcessId;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.TryGetProperty("state", out var s) && s.GetString() == "ended")
                    Ended?.Invoke();
            }
            catch { /* ignore malformed messages */ }
        };
        core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            await core.ExecuteScriptAsync(PlayerAssets.FillPlayAndReportScript);
            _ready.TrySetResult();
        };

        Activate(); // map the window so the renderer composites
        SetForegroundWindow(Hwnd); // bring it to the front so WGC sees live updates
        core.Navigate(watchUrl);
        await _ready.Task;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Seek the player (rewind), using the player API rather than raw currentTime (mac §6).</summary>
    public async Task SeekAsync(double seconds)
    {
        if (Web.CoreWebView2 is { } core)
            await core.ExecuteScriptAsync(PlayerAssets.SeekScript(seconds));
    }

    /// <summary>Read the player's progress/DVR state JSON for the rewind UI.</summary>
    public async Task<string?> ProgressStateAsync()
    {
        if (Web.CoreWebView2 is { } core)
            return await core.ExecuteScriptAsync(PlayerAssets.ProgressStateScript);
        return null;
    }
}
