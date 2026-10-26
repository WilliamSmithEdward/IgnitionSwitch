using Microsoft.UI.Xaml.Controls;

namespace IgnitionSwitch.App.Presentation;

internal sealed record TrackControls(
    Button Button,
    TextBlock Name,
    TextBlock Detail,
    Slider Volume
);
