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
    private readonly Canvas canvas = new();
    private readonly RectangleGeometry clip = new();
    private readonly List<LaneVisual> lanes = [];
    private readonly List<Border> beatLines = [];
    private readonly List<TextBlock> barLabels = [];
    private readonly Border playhead = new()
    {
        Width = 2,
        Background = InstrumentControls.Brush("#FFB05C"),
        IsHitTestVisible = false
    };
    private readonly HashSet<LoopNote> seenNotes = [];
    private readonly List<LoopNote> removedNotes = [];
    private IReadOnlyList<TrackState> tracks = [];
    private double beats = 16;
    private double position;
    private double width;
    private double height;
    private int selectedTrack;

    public LoopTimeline(Grid host, Action<int> selectTrack)
    {
        this.host = host;
        this.selectTrack = selectTrack;
        canvas.Clip = clip;
        host.Children.Add(canvas);
        host.SizeChanged += (_, _) => Arrange();
    }

    public void Render(SessionSnapshot session, IReadOnlyList<TrackState> trackStates, int selected)
    {
        double newBeats = session.LoopBars * 4;

        if (lanes.Count != session.Tracks.Count || beats != newBeats)
        {
            beats = newBeats;
            CreateVisuals(session.Tracks.Count);
        }

        UpdateTracks(trackStates, selected);

        for (int index = 0; index < lanes.Count; index++)
        {
            LaneVisual lane = lanes[index];
            seenNotes.Clear();

            foreach (LoopNote note in session.Tracks[index].Notes)
            {
                _ = seenNotes.Add(note);

                if (!lane.Notes.TryGetValue(note, out NoteVisual? visual))
                {
                    visual = new NoteVisual();
                    lane.Notes.Add(note, visual);
                    lane.Geometry.Children.Add(visual.First);
                    lane.Geometry.Children.Add(visual.Wrapped);
                    ArrangeNote(index, note, visual);
                }
            }

            removedNotes.Clear();

            foreach (LoopNote note in lane.Notes.Keys)
            {
                if (!seenNotes.Contains(note))
                {
                    removedNotes.Add(note);
                }
            }

            foreach (LoopNote note in removedNotes)
            {
                NoteVisual visual = lane.Notes[note];
                _ = lane.Geometry.Children.Remove(visual.First);
                _ = lane.Geometry.Children.Remove(visual.Wrapped);
                _ = lane.Notes.Remove(note);
            }
        }

        if (width != host.ActualWidth || height != host.ActualHeight)
        {
            Arrange();
        }
    }

    public void UpdateTracks(IReadOnlyList<TrackState> trackStates, int selected)
    {
        tracks = trackStates;
        selectedTrack = selected;

        for (int index = 0; index < lanes.Count; index++)
        {
            LaneVisual lane = lanes[index];
            TrackState track = tracks[index];
            lane.Label.Text = track.Name;
            lane.Label.Foreground = InstrumentControls.Brush(track.ColorHex);
            lane.Label.Opacity = track.IsMuted ? 0.45 : 1;
            lane.Button.Background = InstrumentControls.Brush(
                index == selectedTrack ? "#252B38" : "#151A23"
            );
            lane.Path.Fill = InstrumentControls.Brush(track.ColorHex);
            lane.Path.Opacity = track.IsMuted ? 0.3 : 0.9;
            AutomationProperties.SetName(lane.Button, $"Select {track.Name} loop lane");
        }
    }

    public void UpdatePosition(double beat, bool playing)
    {
        position = beat;
        Canvas.SetLeft(playhead, LabelWidth + (beat / beats * Math.Max(0, width - LabelWidth)));
        playhead.Opacity = playing ? 1 : 0.45;
    }

    private void CreateVisuals(int count)
    {
        canvas.Children.Clear();
        lanes.Clear();
        beatLines.Clear();
        barLabels.Clear();

        for (int index = 0; index < count; index++)
        {
            int trackIndex = index;
            var lane = new LaneVisual();
            lane.Button.Click += (_, _) => selectTrack(trackIndex);
            lanes.Add(lane);
            canvas.Children.Add(lane.Button);
        }

        for (int beat = 0; beat <= beats; beat++)
        {
            var line = new Border
            {
                Width = 1,
                Background = InstrumentControls.Brush(beat % 4 == 0 ? "#414958" : "#282E39"),
                IsHitTestVisible = false
            };
            beatLines.Add(line);
            canvas.Children.Add(line);

            if (beat % 4 == 0 && beat < beats)
            {
                var label = new TextBlock
                {
                    Text = ((beat / 4) + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    Foreground = InstrumentControls.Brush("#9CA8BA"),
                    IsHitTestVisible = false
                };
                barLabels.Add(label);
                canvas.Children.Add(label);
            }
        }

        foreach (LaneVisual lane in lanes)
        {
            canvas.Children.Add(lane.Path);
        }

        canvas.Children.Add(playhead);
        Arrange();
    }

    private void Arrange()
    {
        width = host.ActualWidth;
        height = host.ActualHeight;
        clip.Rect = new Rect(0, 0, width, height);

        if (lanes.Count == 0 || width <= LabelWidth || height <= RulerHeight)
        {
            return;
        }

        double gridWidth = width - LabelWidth;
        double laneHeight = (height - RulerHeight) / lanes.Count;

        for (int index = 0; index < lanes.Count; index++)
        {
            LaneVisual lane = lanes[index];
            lane.Button.Width = width;
            lane.Button.Height = Math.Max(1, laneHeight - 2);
            Canvas.SetTop(lane.Button, RulerHeight + (index * laneHeight));

            foreach ((LoopNote note, NoteVisual visual) in lane.Notes)
            {
                ArrangeNote(index, note, visual);
            }
        }

        for (int beat = 0; beat < beatLines.Count; beat++)
        {
            Border line = beatLines[beat];
            line.Height = height - RulerHeight;
            Canvas.SetLeft(line, Math.Min(width - 1, LabelWidth + (beat / beats * gridWidth)));
            Canvas.SetTop(line, RulerHeight);
        }

        for (int bar = 0; bar < barLabels.Count; bar++)
        {
            Canvas.SetLeft(barLabels[bar], LabelWidth + (bar * 4 / beats * gridWidth) + 6);
        }

        playhead.Height = height - RulerHeight;
        Canvas.SetTop(playhead, RulerHeight);
        Canvas.SetLeft(playhead, LabelWidth + (position / beats * gridWidth));
    }

    private void ArrangeNote(int index, LoopNote note, NoteVisual visual)
    {
        double gridWidth = Math.Max(0, width - LabelWidth);
        double laneHeight = Math.Max(1, (height - RulerHeight) / lanes.Count);
        double top = RulerHeight
            + (index * laneHeight)
            + 6
            + ((1 - (note.Note % 12 / 11.0)) * Math.Max(0, laneHeight - 20));
        double left = note.Beat / beats * gridWidth;
        double duration = note.Duration / beats * gridWidth;
        double visible = Math.Max(0, Math.Min(Math.Max(3, duration), gridWidth - left));
        visual.First.Rect = new Rect(LabelWidth + left, top, visible, 7);
        visual.Wrapped.Rect = new Rect(
            LabelWidth,
            top,
            note.Beat + note.Duration > beats ? Math.Max(0, duration - (gridWidth - left)) : 0,
            7
        );
    }

    private sealed class NoteVisual
    {
        public RectangleGeometry First { get; } = new();
        public RectangleGeometry Wrapped { get; } = new();
    }

    private sealed class LaneVisual
    {
        public LaneVisual()
        {
            Button.Content = Label;
            Path.Data = Geometry;
        }

        public Button Button { get; } = new()
        {
            Padding = new Thickness(10, 0, 0, 0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        public TextBlock Label { get; } = new()
        {
            Width = LabelWidth - 18,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        public GeometryGroup Geometry { get; } = new() { FillRule = FillRule.Nonzero };
        public Microsoft.UI.Xaml.Shapes.Path Path { get; } = new() { IsHitTestVisible = false };
        public Dictionary<LoopNote, NoteVisual> Notes { get; } = [];
    }
}
