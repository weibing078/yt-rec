using Microsoft.UI.Xaml;

namespace YtRec.App;

/// <summary>Small x:Bind function helpers (avoids value-converter boilerplate).</summary>
public static class XamlHelpers
{
    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisCount(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
