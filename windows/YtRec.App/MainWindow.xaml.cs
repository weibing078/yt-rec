using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using YtRec.Core;

namespace YtRec.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // Native Win11 material so the window doesn't read as a flat/unfinished panel.
        SystemBackdrop = new MicaBackdrop();

        // Custom title bar: brand + commands on the left, system caption buttons on the right.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        Title = "YT Rec";
        try { AppWindow.SetIcon("Assets\\AppIcon.ico"); } catch { /* icon optional */ }
        AppWindow.Resize(new SizeInt32(480, 720));

        // Pre-start disk guard's warn band (8–15 GB): the ViewModel asks the View to confirm before recording.
        Vm.ConfirmContinueLowDiskAsync = async free =>
        {
            var dlg = new ContentDialog
            {
                Title = "磁碟空間偏低",
                Content = $"輸出磁碟只剩約 {MainViewModel.FormatBytes(free)}，側錄可能很快就會因空間不足自動存檔。仍要繼續嗎？",
                PrimaryButtonText = "繼續側錄",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            try { return await dlg.ShowAsync() == ContentDialogResult.Primary; }
            catch { return false; }
        };

        // Disaster recovery: rebuild any side-record interrupted by a crash/kill (off the UI thread).
        _ = Vm.RecoverOrphansAsync();
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        // Clipboard.GetContent()/GetTextAsync() throw COMException when another app holds the clipboard open
        // (Win+V, clipboard managers, RDP) — a common race. This is async void, so an escape would crash the app.
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
                Vm.UrlText = (await content.GetTextAsync()).Trim();
        }
        catch { try { await Info("貼上失敗", "無法讀取剪貼簿（可能被其他程式占用），請再貼一次。"); } catch { } }
    }

    private async void OnPermissions(object sender, RoutedEventArgs e)
    {
        try
        {
            await Info("權限 / Permissions",
                "側錄（螢幕擷取）用的是 Windows.Graphics.Capture，會在第一次擷取時由系統確認，不需事先授權。" +
                "下載軌不需要任何權限。");
        }
        catch { /* dialog already open / transient — ignore */ }
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
      try
      {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Items.Add("3 小時");
        combo.Items.Add("6 小時（預設）");
        combo.Items.Add("12 小時");
        combo.Items.Add("不限");
        combo.SelectedIndex = Vm.DurationCap switch
        {
            DurationCap.ThreeHours => 0,
            DurationCap.SixHours => 1,
            DurationCap.TwelveHours => 2,
            _ => 3,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = "側錄時間上限（到上限自動存檔）", FontSize = 13 });
        panel.Children.Add(combo);
        panel.Children.Add(new TextBlock
        {
            Text = "輸出資料夾：" + OutputPaths.Root,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        if (Vm.HasAudioNotice)
            panel.Children.Add(new TextBlock { Text = Vm.AudioNotice, FontSize = 12, TextWrapping = TextWrapping.Wrap });

        var dlg = new ContentDialog
        {
            Title = "設定 / Settings",
            Content = panel,
            PrimaryButtonText = "儲存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            Vm.DurationCap = combo.SelectedIndex switch
            {
                0 => DurationCap.ThreeHours,
                1 => DurationCap.SixHours,
                2 => DurationCap.TwelveHours,
                _ => DurationCap.Unlimited,
            };
      }
      catch { /* dialog already open / transient — ignore rather than crash (async void) */ }
    }

    // Drag a recent output straight into Premiere (or any app that accepts file drops).
    private void OnDragRecent(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 0 || e.Items[0] is not RecentFile rf || !File.Exists(rf.FullPath)) { e.Cancel = true; return; }
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(rf.FullPath);
                request.SetData(new List<IStorageItem> { file });
            }
            catch { /* file vanished */ }
            finally { deferral.Complete(); }
        });
    }

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        // TOCTOU: the file can be moved/deleted between File.Exists and GetFileFromPathAsync (Premiere workflow),
        // which throws on the async-void continuation → crash. Guard it and tell the user instead.
        try
        {
            if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
                await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        }
        catch { try { await Info("無法開啟", "這個檔案可能已經被移動或刪除了。"); } catch { } }
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(OutputPaths.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputPaths.Root}\"") { UseShellExecute = true });
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var logs = Path.Combine(OutputPaths.Root, "logs");
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logs}\"") { UseShellExecute = true });
    }

    private void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        var url = Vm.UpdateUrl;
        if (string.IsNullOrEmpty(url)) return;
        // UseShellExecute runs the shell's default action on the string — a tampered latest.json could hand us a
        // local path / UNC / .exe that would be EXECUTED, not opened. Only ever hand the shell http(s).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
            (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private async Task Info(string title, string message) =>
        await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        }.ShowAsync();
}
