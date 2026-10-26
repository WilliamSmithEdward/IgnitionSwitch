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

    public static (Button Button, TextBlock Detail, Slider Volume) CreateTrack(
        TrackState track,
        int index
    )
    {
        var detail = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7
        };
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(new TextBlock
        {
            Text = $"0{index + 1}  {track.Name}",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(track.ColorHex)
        });
        content.Children.Add(detail);
        var button = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["TrackButtonStyle"]
        };
        AutomationProperties.SetName(button, $"Select {track.Name}");
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 0.01,
            Value = track.Volume,
            Margin = new Thickness(10, -5, 10, 2)
        };
        AutomationProperties.SetName(slider, $"{track.Name} volume");
        return (button, detail, slider);
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
            var content = new StackPanel
            {
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString("00", CultureInfo.InvariantCulture),
                FontSize = 10,
                Opacity = 0.6
            });
            content.Children.Add(new TextBlock
            {
                Text = DrumNames[index],
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
            var button = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources["PadButtonStyle"],
                Padding = new Thickness(7)
            };
            Grid.SetRow(button, index < 8 ? 1 : 0);
            Grid.SetColumn(button, index % 8);
            AutomationProperties.SetName(button, $"Play {DrumNames[index]}");
            ToolTipService.SetToolTip(button, $"{DrumNames[index]} · MIDI {note}");
            AttachNotes(button, () => 3, note, noteOn, noteOff);
            grid.Children.Add(button);
        }
    }

    public static void CreateKeyboard(
        Grid grid,
        Func<int> selectedTrack,
        Action<int, int> noteOn,
        Action<int, int> noteOff
    )
    {
        string[] noteNames = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
        var blackKeys = new List<(Button Button, int AfterWhite)>();
        var overlay = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Top,
            Height = 60
        };
        int whiteIndex = -1;

        for (int note = 48; note <= 72; note++)
        {
            bool black = note % 12 is 1 or 3 or 6 or 8 or 10;
            var button = new Button
            {
                Content = black ? string.Empty : noteNames[note % 12],
                Background = Brush(black ? "#252D3D" : "#DFE6F2"),
                Foreground = Brush("#3F495C"),
                BorderBrush = Brush("#10131B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0, 0, 5, 5),
                Padding = new Thickness(0, 0, 0, 8),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Bottom
            };
            AutomationProperties.SetName(button, $"Play {noteNames[note % 12]}{(note / 12) - 1}");
            AttachNotes(button, selectedTrack, note, noteOn, noteOff);

            if (black)
            {
                button.Height = 60;
                blackKeys.Add((button, whiteIndex));
                overlay.Children.Add(button);
            }

            else
            {
                whiteIndex++;
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(button, whiteIndex);
                grid.Children.Add(button);
            }
        }

        Grid.SetColumnSpan(overlay, whiteIndex + 1);
        grid.Children.Add(overlay);
        grid.SizeChanged += (_, _) =>
        {
            double width = grid.ActualWidth / (whiteIndex + 1);

            foreach ((Button button, int afterWhite) in blackKeys)
            {
                button.Width = width * 0.62;
                Canvas.SetLeft(button, ((afterWhite + 1) * width) - (button.Width / 2));
            }
        };
    }

    private static void AttachNotes(
        Button button,
        Func<int> trackSelector,
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
                owner = trackSelector();
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

            int track = trackSelector();
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
