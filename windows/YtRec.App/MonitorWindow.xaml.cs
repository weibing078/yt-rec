using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;

namespace YtRec.App;

/// <summary>The floating monitor (viewfinder). Always-on-top, draggable, resizable; "shrink to background"
/// moves it off into a corner rather than minimizing (a minimized capture target freezes WGC — but this
/// window is NOT the capture target, the off-screen PlayerWindow is, so shrinking is purely cosmetic).</summary>
public sealed partial class MonitorWindow : Window
{
    public event Action? StopRequested;

    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _timer;
    private DateTime _start;
    private WriteableBitmap? _wb;
    private bool _shrunk;
    private RectInt32 _normalRect;

    public MonitorWindow(string title)
    {
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        TitleText.Text = title;
        Title = "YT Rec";

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;
            p.SetBorderAndTitleBar(true, false);
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Bar);
        AppWindow.Resize(new SizeInt32(360, 280));
        // Sit bottom-right, clear of the player window (which is captured at the top-left).
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + area.Width - 392, area.Y + area.Height - 312));

        _start = DateTime.Now;
        _timer = _ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            var t = DateTime.Now - _start;
            ElapsedText.Text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        };
        _timer.Start();
    }

    public void SetStatus(string text) => _ui.TryEnqueue(() => StatusText.Text = text);

    /// <summary>Render a captured BGRA frame into the preview (called off-thread; marshals to the UI).</summary>
    public void UpdatePreview(byte[] bgra, int w, int h)
    {
        _ui.TryEnqueue(() =>
        {
            if (_wb is null || _wb.PixelWidth != w || _wb.PixelHeight != h)
            {
                _wb = new WriteableBitmap(w, h);
                Preview.Source = _wb;
            }
            using var s = _wb.PixelBuffer.AsStream();
            s.Write(bgra, 0, Math.Min(bgra.Length, (int)s.Length));
            _wb.Invalidate();
        });
    }

    private void OnShrink(object sender, RoutedEventArgs e)
    {
        if (!_shrunk)
        {
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            _normalRect = new RectInt32(pos.X, pos.Y, size.Width, size.Height);
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Resize(new SizeInt32(220, 70));
            AppWindow.Move(new PointInt32(area.X + area.Width - 240, area.Y + area.Height - 90));
            ShrinkButton.Content = "+";
            _shrunk = true;
        }
        else
        {
            if (_normalRect.Width > 0) { AppWindow.Move(new PointInt32(_normalRect.X, _normalRect.Y)); AppWindow.Resize(new SizeInt32(_normalRect.Width, _normalRect.Height)); }
            ShrinkButton.Content = "–";
            _shrunk = false;
        }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        StopRequested?.Invoke();
    }

    public void CloseMonitor()
    {
        _timer.Stop();
        Close();
    }
}
