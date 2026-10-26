using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IgnitionSwitch.App.Presentation;

internal static class FlyoutViewport
{
    public static void Update(ScrollViewer scroll, FrameworkElement owner)
    {
        if (owner.XamlRoot is { } root)
        {
            scroll.MaxHeight = Math.Max(100, Math.Min(560, root.Size.Height - 96));
        }
    }
}
