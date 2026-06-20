using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace YtRec.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar: brand + commands on the left, system caption buttons on the right.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        Title = "YT Rec";
        AppWindow.Resize(new SizeInt32(500, 760));
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Text))
            Vm.UrlText = (await content.GetTextAsync()).Trim();
    }

    private async void OnPermissions(object sender, RoutedEventArgs e) =>
        await Info("權限 / Permissions", "螢幕錄製權限會在 Phase 2（螢幕側錄）需要。下載軌不需要特別權限。");

    private async void OnSettings(object sender, RoutedEventArgs e) =>
        await Info("設定 / Settings", "設定頁（輸出資料夾、畫質、語言切換）將於後續加入。");

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
