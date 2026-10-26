using System.Globalization;
using IgnitionSwitch.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace IgnitionSwitch.App.Presentation;

internal sealed class LoopTimeline
{
    private const double LabelWidth = 116;
    private const double RulerHeight = 24;
    private readonly Grid host;
    private readonly Action<int> selectTrack;
    private SessionSnapshot? snapshot;
    private IReadOnlyList<TrackState> tracks = [];
    private int selectedTrack;
    private Border? playhead;
    private double position;
    private bool playing;

    public LoopTimeline(Grid host, Action<int> selectTrack)
    {
        this.host = host;
        this.selectTrack = selectTrack;
        host.SizeChanged += (_, _) => Draw();
    }

    public void Render(SessionSnapshot session, IReadOnlyList<TrackState> trackStates, int selected)
    {
        snapshot = session;
        tracks = trackStates;
        selectedTrack = selected;
        Draw();
    }

    public void UpdatePosition(double beat, bool isPlaying)
    {
        position = beat;
        playing = isPlaying;

        if (playhead is null || snapshot is null)
        {
            return;
        }

        double width = Math.Max(0, host.ActualWidth - LabelWidth);
        Canvas.SetLeft(playhead, LabelWidth + (beat / (snapshot.LoopBars * 4) * width));
        playhead.Opacity = playing ? 1 : 0.45;
    }

    private void Draw()
    {
        host.Children.Clear();
        playhead = null;

        if (snapshot is null || host.ActualWidth <= LabelWidth || host.ActualHeight <= RulerHeight)
        {
            return;
        }

        double width = host.ActualWidth;
        double height = host.ActualHeight;
        double gridWidth = width - LabelWidth;
        double laneHeight = (height - RulerHeight) / snapshot.Tracks.Count;
        double beats = snapshot.LoopBars * 4;
        var canvas = new Canvas
        {
            Width = width,
            Height = height
        };
        host.Children.Add(canvas);

        for (int index = 0; index < snapshot.Tracks.Count; index++)
        {
            AddLane(canvas, index, width, laneHeight, gridWidth, beats);
        }

        for (int beat = 0; beat <= beats; beat++)
        {
            double left = LabelWidth + (beat / beats * gridWidth);
            bool barLine = beat % 4 == 0;
            var line = new Border
            {
                Width = 1,
                Height = height - RulerHeight,
                Background = InstrumentControls.Brush(barLine ? "#414958" : "#282E39"),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(line, left);
            Canvas.SetTop(line, RulerHeight);
            canvas.Children.Add(line);

            if (barLine && beat < beats)
            {
                var label = new TextBlock
                {
                    Text = ((beat / 4) + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    Foreground = InstrumentControls.Brush("#9CA8BA"),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, left + 6);
                canvas.Children.Add(label);
            }
        }

        playhead = new Border
        {
            Width = 2,
            Height = height - RulerHeight,
            Background = InstrumentControls.Brush("#FFB05C"),
            IsHitTestVisible = false
        };
        Canvas.SetTop(playhead, RulerHeight);
        canvas.Children.Add(playhead);
        UpdatePosition(position, playing);
    }

    private void AddLane(
        Canvas canvas,
        int index,
        double width,
        double height,
        double gridWidth,
        double beats
    )
    {
        TrackSnapshot track = snapshot!.Tracks[index];
        string color = tracks[index].ColorHex;
        double top = RulerHeight + (index * height);
        var lane = new Button
        {
            Width = width,
            Height = height - 2,
            Padding = new Thickness(10, 0, 0, 0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = InstrumentControls.Brush(index == selectedTrack ? "#252B38" : "#151A23"),
            Content = new TextBlock
            {
                Text = track.Name,
                Width = LabelWidth - 18,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = InstrumentControls.Brush(color),
                Opacity = track.IsMuted ? 0.45 : 1,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        lane.Click += (_, _) => selectTrack(index);
        AutomationProperties.SetName(lane, $"Select {track.Name} loop lane");
        Canvas.SetTop(lane, top);
        canvas.Children.Add(lane);
        var geometry = new GeometryGroup();

        foreach (LoopNote note in track.Notes)
        {
            double noteTop = top + 6 + ((1 - (note.Note % 12 / 11.0)) * Math.Max(0, height - 20));
            double length = Math.Max(3, note.Duration / beats * gridWidth);
            double left = note.Beat / beats * gridWidth;
            double visible = Math.Min(length, gridWidth - left);
            geometry.Children.Add(
                new RectangleGeometry { Rect = new Rect(LabelWidth + left, noteTop, visible, 7) }
            );

            if (length > visible)
            {
                geometry.Children.Add(
                    new RectangleGeometry
                    {
                        Rect = new Rect(
                            LabelWidth,
                            noteTop,
                            Math.Min(length - visible, gridWidth),
                            7
                        )
                    }
                );
            }
        }

        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = InstrumentControls.Brush(color),
            Opacity = track.IsMuted ? 0.3 : 0.9,
            IsHitTestVisible = false
        });
    }
}
