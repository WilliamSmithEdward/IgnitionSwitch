using System.Globalization;
using IgnitionSwitch.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace IgnitionSwitch.App.Presentation;

internal static class InstrumentControls
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly string[] DrumNames = [
        "Kick", "Rim", "Snare", "Clap", "Snare II", "Low tom", "Closed hat", "Tom", "Pedal hat",
        "Low percussion", "Open hat", "Mid tom", "High tom", "Cymbal", "High percussion", "Ride"
    ];

    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out SolidColorBrush? cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(
            Color.FromArgb(
                255,
                byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            )
        );
        Brushes.Add(hex, brush);
        return brush;
    }

    public static TrackControls CreateTrack(TrackState track, int index)
    {
        var detail = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7
        };
        var content = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 3
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition());
        var number = new TextBlock
        {
            Text = (index + 1).ToString("00", CultureInfo.InvariantCulture),
            FontSize = 11,
            Foreground = Brush(track.ColorHex),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRowSpan(number, 2);
        content.Children.Add(number);
        var name = new TextBlock
        {
            Text = track.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        content.Children.Add(name);
        Grid.SetRow(detail, 1);
        Grid.SetColumn(detail, 1);
        content.Children.Add(detail);
        var button = new Button
        {
            Content = content,
            Height = 60,
            Style = (Style)Application.Current.Resources["TrackButtonStyle"]
        };
        AutomationProperties.SetName(button, $"Select {track.Name}");
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 0.01,
            Value = track.Volume,
            Height = 26,
            MinHeight = 26,
            Margin = new Thickness(34, 0, 12, 0)
        };
        AutomationProperties.SetName(slider, $"{track.Name} volume");
        return new TrackControls(button, name, detail, slider);
    }

    public static IReadOnlyList<PadControls> CreatePads(
        Grid grid,
        Action<int, int> noteOn,
        Action<int, int> noteOff,
        IReadOnlyList<int>? noteMap = null,
        int columns = 8
    )
    {
        int count = noteMap?.Count ?? 16;

        if (count is < 1 or > 128 || noteMap?.Any(note => note is < 0 or > 127) == true)
        {
            throw new ArgumentException(
                "A pad map must contain 1–128 valid MIDI notes.",
                nameof(noteMap)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, count);
        var pads = new List<PadControls>(count);

        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        int rows = (count + columns - 1) / columns;

        for (int row = 0; row < rows; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }

        for (int index = 0; index < count; index++)
        {
            int note = noteMap?[index] ?? (index + 36);
            string name = note is >= 36 and <= 51 ? DrumNames[note - 36] : $"Note {note}";
            var content = new Grid();
            var feedback = new Border { IsHitTestVisible = false };
            content.Children.Add(feedback);
            content.Children.Add(new TextBlock
            {
                Text = name.Replace("percussion", "perc.", StringComparison.Ordinal),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(12, 8, 12, 8),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            var button = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources["PadButtonStyle"]
            };
            Grid.SetRow(button, index / columns);
            Grid.SetColumn(button, index % columns);
            AutomationProperties.SetName(button, $"Play {name}");
            ToolTipService.SetToolTip(button, $"{name} · MIDI {note}");
            pads.Add(new PadControls(note, button, feedback, noteOn, noteOff));
            grid.Children.Add(button);
        }

        return pads;
    }
}
