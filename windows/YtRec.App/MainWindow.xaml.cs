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
        AppWindow.Resize(new SizeInt32(480, 720));

        // Disaster recovery: rebuild any side-record interrupted by a crash/kill (off the UI thread).
        _ = Vm.RecoverOrphansAsync();
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Text))
            Vm.UrlText = (await content.GetTextAsync()).Trim();
    }

    private async void OnPermissions(object sender, RoutedEventArgs e) =>
        await Info("權限 / Permissions",
            "側錄（螢幕擷取）用的是 Windows.Graphics.Capture，會在第一次擷取時由系統確認，不需事先授權。" +
            "下載軌不需要任何權限。");

    private async void OnSettings(object sender, RoutedEventArgs e)
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
        if (sender is FrameworkElement { Tag: string path } && File.Exists(path))
            await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
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

    private async Task Info(string title, string message) =>
        await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        }.ShowAsync();
}
