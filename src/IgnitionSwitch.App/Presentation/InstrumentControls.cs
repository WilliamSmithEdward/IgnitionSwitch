using System.Globalization;
using IgnitionSwitch.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace IgnitionSwitch.App.Presentation;

internal static class InstrumentControls
{
    private static readonly string[] DrumNames = [
        "Kick", "Rim", "Snare", "Clap", "Snare II", "Low tom", "Closed hat", "Tom", "Pedal hat",
        "Low percussion", "Open hat", "Mid tom", "High tom", "Cymbal", "High percussion", "Ride"
    ];

    public static SolidColorBrush Brush(string hex)
    {
        return new(
            Color.FromArgb(
                255,
                byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            )
        );
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

    public static void CreatePads(Grid grid, Action<int, int> noteOn, Action<int, int> noteOff)
    {
        for (int column = 0; column < 8; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        for (int index = 0; index < 16; index++)
        {
            int note = index + 36;
            var content = new TextBlock
            {
                Text = DrumNames[index],
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources["PadButtonStyle"],
                Padding = new Thickness(12)
            };
            Grid.SetRow(button, index < 8 ? 1 : 0);
            Grid.SetColumn(button, index % 8);
            AutomationProperties.SetName(button, $"Play {DrumNames[index]}");
            ToolTipService.SetToolTip(button, $"{DrumNames[index]} · MIDI {note}");
            AttachNotes(button, 3, note, noteOn, noteOff);
            grid.Children.Add(button);
        }
    }

    private static void AttachNotes(
        Button button,
        int track,
        int note,
        Action<int, int> noteOn,
        Action<int, int> noteOff
    )
    {
        int owner = -1;
        bool pointerClick = false;

        void Release()
        {
            if (owner < 0)
            {
                return;
            }

            noteOff(owner, note);
            owner = -1;
        }

        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
            {
                if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                Release();
                pointerClick = true;
                owner = track;
                noteOn(owner, note);
                _ = button.CapturePointer(args.Pointer);
            }), true);
        button.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => Release()),
            true
        );
        button.PointerCaptureLost += (_, _) => Release();
        button.Click += (_, _) =>
        {
            if (pointerClick)
            {
                pointerClick = false;
                return;
            }

            noteOn(track, note);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                noteOff(track, note);
            };
            timer.Start();
        };
    }
}
