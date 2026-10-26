using System.Runtime.InteropServices;
using System.Text.Json;
using IgnitionSwitch.App.Audio;
using IgnitionSwitch.App.Presentation;
using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace IgnitionSwitch.App.Views;

/// <summary>The standalone recording and performance workspace.</summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly StudioSession session;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly List<(Button Button, TextBlock Detail, Slider Volume)> trackControls = [];
    private bool updating;
    private bool closingAllowed;
    private bool dialogOpen;
    private bool disposed;
    private string timelineKey = string.Empty;

    /// <summary>Creates the workspace and initializes its audio and hardware services on load.</summary>
    public MainWindow()
    {
        var engine = new WorkstationEngine();
        session = new StudioSession(
            ControllerFactory.CreateDefault(),
            engine,
            new AudioPlayback(engine)
        );
        InitializeComponent();
        BuildInstruments();
        WireControls();
        timer.Tick += (_, _) => RefreshState();
        ((FrameworkElement)Content).Loaded += (_, _) =>
        {
            ResizeWorkspace();
            session.Start();
            RefreshState();
            timer.Start();
        };
        AppWindow.Closing += OnClosing;
        Closed += (_, _) =>
        {
            Dispose();
        };
    }

    /// <summary>Stops the audio timer and releases the device and music engine.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        session.Dispose();
        GC.SuppressFinalize(this);
    }

    private void BuildInstruments()
    {
        for (int index = 0; index < WorkstationEngine.TrackCount; index++)
        {
            int trackIndex = index;
            (
                Button Button,
                TextBlock Detail,
                Slider Volume
            ) controls = InstrumentControls.CreateTrack(session.Engine.Tracks[index], index);
            controls.Button.Click += (_, _) =>
            {
                session.SelectTrack(trackIndex);
                RefreshState();
            };
            controls.Volume.ValueChanged += (_, args) =>
            {
                if (!updating)
                {
                    session.Engine.SetTrackVolume(trackIndex, (float)args.NewValue);
                    session.MarkChanged();
                }
            };
            trackControls.Add(controls);
            var panel = new StackPanel { Spacing = 0 };
            panel.Children.Add(controls.Button);
            panel.Children.Add(controls.Volume);
            TrackPanel.Children.Add(panel);
        }

        InstrumentControls.CreatePads(PadPanel, PreviewNoteOn, PreviewNoteOff);
        InstrumentControls.CreateKeyboard(
            KeyboardPanel,
            () => session.Engine.SelectedTrack,
            PreviewNoteOn,
            PreviewNoteOff
        );
        TimelinePanel.SizeChanged += (_, _) => DrawTimeline();
    }

    private void ResizeWorkspace()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        double scale = Content.XamlRoot.RasterizationScale;
        int width = Math.Min((int)(1440 * scale), display.WorkArea.Width);
        int height = Math.Min((int)(960 * scale), display.WorkArea.Height);
        AppWindow.MoveAndResize(
            new Windows.Graphics.RectInt32(
                display.WorkArea.X + ((display.WorkArea.Width - width) / 2),
                display.WorkArea.Y + ((display.WorkArea.Height - height) / 2),
                width,
                height
            )
        );
    }

    private void WireControls()
    {
        PlayButton.Click += (_, _) =>
        {
            session.TogglePlayback();
            RefreshState();
        };
        RecordButton.Click += (_, _) =>
        {
            session.ToggleRecording();
            RefreshState();
        };
        StopButton.Click += (_, _) =>
        {
            session.Stop();
            RefreshState();
        };
        ReconnectButton.Click += (_, _) =>
        {
            session.Stop();
            session.Controller.Disconnect();
            session.Audio.Start();
            session.Controller.Connect();
        };
        TempoBox.ValueChanged += (_, args) =>
        {
            if (!updating && double.IsFinite(args.NewValue))
            {
                session.Engine.Tempo = Math.Clamp(args.NewValue, 60, 180);
                session.MarkChanged();
            }
        };
        MasterSlider.ValueChanged += (_, args) =>
        {
            if (!updating)
            {
                session.Engine.MasterVolume = (float)args.NewValue;
                session.MarkChanged();
            }
        };
        MetronomeToggle.Click += (_, _) =>
            session.Engine.MetronomeEnabled = MetronomeToggle.IsChecked == true;
        MuteButton.Click += (_, _) =>
        {
            session.Engine.SetTrackMuted(
                session.Engine.SelectedTrack,
                MuteButton.IsChecked == true
            );
            session.MarkChanged();
        };
        ClearButton.Click += async (_, _) => await ClearTrackAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        OpenButton.Click += async (_, _) => await OpenAsync();
        AddShortcut(VirtualKey.Space, session.TogglePlayback);
        AddShortcut(VirtualKey.R, session.ToggleRecording);
        AddShortcut(VirtualKey.Escape, session.Stop);
    }

    private void AddShortcut(VirtualKey key, Action action)
    {
        var shortcut = new KeyboardAccelerator { Key = key };
        shortcut.Invoked += (_, args) =>
        {
            if (
                dialogOpen
                || FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or NumberBox
            )
            {
                return;
            }

            action();
            args.Handled = true;
            RefreshState();
        };
        Content.KeyboardAccelerators.Add(shortcut);
    }

    private void PreviewNoteOn(int track, int note)
    {
        session.Engine.NoteOn(track, note, 100);

        if (session.Engine.IsRecording)
        {
            session.MarkChanged();
        }
    }

    private void PreviewNoteOff(int track, int note)
    {
        if (!disposed)
        {
            session.Engine.NoteOff(track, note);

            if (session.Engine.IsRecording)
            {
                session.MarkChanged();
            }
        }
    }

    private void RefreshState()
    {
        updating = true;
        session.Audio.EnsureRunning();
        WorkstationEngine engine = session.Engine;

        if (!session.Audio.IsRunning && engine.IsPlaying)
        {
            session.Stop();
        }

        IReadOnlyList<TrackState> tracks = engine.Tracks;
        ConnectionText.Text = session.Controller.Status;
        AudioText.Text = session.Audio.Status;
        PlayButton.IsEnabled = session.Audio.IsRunning;
        RecordButton.IsEnabled = session.Audio.IsRunning;
        PlayButton.Content = engine.IsPlaying ? "■  Stop" : "▶  Play";
        RecordButton.Content = engine.IsRecording ? "●  Recording" : "●  Record";
        TempoBox.Value = engine.Tempo;
        MasterSlider.Value = engine.MasterVolume;
        MetronomeToggle.IsChecked = engine.MetronomeEnabled;
        MuteButton.IsChecked = tracks[engine.SelectedTrack].IsMuted;
        SelectedTrackText.Text = tracks[engine.SelectedTrack].Name;
        LoopProgress.Value = engine.BeatPosition;
        PositionText.Text = engine.IsPlaying
            ? $"Bar {(int)(engine.BeatPosition / 4) + 1} · beat {(int)(engine.BeatPosition % 4) + 1}"
            : "Ready · 4 bars";

        for (int index = 0; index < tracks.Count; index++)
        {
            TrackState track = tracks[index];
            (Button Button, TextBlock Detail, Slider Volume) controls = trackControls[index];
            controls.Detail.Text =
                $"{track.NoteCount} notes{(track.IsMuted ? " · muted" : string.Empty)}";
            controls.Button.BorderBrush = InstrumentControls.Brush(
                index == engine.SelectedTrack ? track.ColorHex : "#30394A"
            );
            controls.Volume.Value = track.Volume;
        }

        Title = session.HasChanges ? "IgnitionSwitch · Unsaved session" : "IgnitionSwitch";
        string key =
            $"{session.Revision}:{string.Join(',', tracks.Select(track => track.NoteCount))}";

        if (timelineKey != key)
        {
            timelineKey = key;
            DrawTimeline();
        }

        session.RefreshFeedback();
        updating = false;
    }

    private void DrawTimeline()
    {
        TimelinePanel.Children.Clear();

        if (TimelinePanel.ActualWidth <= 0)
        {
            return;
        }

        SessionSnapshot snapshot = session.Engine.CaptureSession();

        for (int track = 0; track < snapshot.Tracks.Count; track++)
        {
            var canvas = new Canvas
            {
                Height = 11,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, track * 14, 0, 0)
            };
            canvas.Children.Add(new Border
            {
                Width = TimelinePanel.ActualWidth,
                Height = 11,
                Background = InstrumentControls.Brush("#121722"),
                CornerRadius = new CornerRadius(2)
            });

            foreach (LoopNote note in snapshot.Tracks[track].Notes)
            {
                double left = note.Beat / 16 * TimelinePanel.ActualWidth;
                double width = Math.Max(
                    2,
                    Math.Min(
                        note.Duration / 16 * TimelinePanel.ActualWidth,
                        TimelinePanel.ActualWidth - left
                    )
                );
                var mark = new Border
                {
                    Width = width,
                    Height = 7,
                    Margin = new Thickness(0, 2, 0, 0),
                    Background = InstrumentControls.Brush(session.Engine.Tracks[track].ColorHex),
                    CornerRadius = new CornerRadius(2)
                };
                Canvas.SetLeft(mark, left);
                canvas.Children.Add(mark);
            }

            TimelinePanel.Children.Add(canvas);
        }
    }

    private async Task ClearTrackAsync()
    {
        int selectedTrack = session.Engine.SelectedTrack;

        if (
            session.Engine.Tracks[selectedTrack].NoteCount == 0
            || await ConfirmAsync(
                "Clear this track?",
                "This removes the recorded notes on the selected track.",
                "Clear track"
            )
        )
        {
            session.SelectTrack(selectedTrack);
            session.Engine.ClearSelectedTrack();
            session.MarkChanged();
            timelineKey = string.Empty;
            StatusText.Text = "Track cleared. Play a new idea when you're ready.";
            RefreshState();
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (dialogOpen)
        {
            return false;
        }

        SetInteractionBlocked(true);
        session.Stop();

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "My first loop.ignition.json",
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeChoices.Add("IgnitionSwitch session", [".json"]);
            InitializePicker(picker);
            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file is null)
            {
                return false;
            }

            (SessionSnapshot Snapshot, long Revision) = session.CaptureForSave();
            await SessionStore.SaveAsync(file.Path, Snapshot);
            session.MarkSaved(Revision);
            StatusText.Text = $"Saved · {file.Name}";
            return true;
        }

        catch (Exception exception) when (IsSessionException(exception))
        {
            StatusText.Text = $"Couldn't save the session. {exception.Message}";
            return false;
        }

        finally
        {
            SetInteractionBlocked(false);
            RefreshState();
        }
    }

    private async Task OpenAsync()
    {
        if (
            session.HasChanges
            && !await ConfirmAsync(
                "Open another session?",
                "Your current session has unsaved changes. Save it first if you want to keep it.",
                "Discard and open"
            )
        )
        {
            return;
        }

        if (dialogOpen)
        {
            return;
        }

        SetInteractionBlocked(true);
        session.Stop();

        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add(".json");
            InitializePicker(picker);
            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();

            if (file is null)
            {
                return;
            }

            SessionSnapshot snapshot = await SessionStore.LoadAsync(file.Path);
            session.Engine.ApplySession(snapshot);
            session.MarkSaved();
            timelineKey = string.Empty;
            StatusText.Text = $"Opened · {file.Name}";
        }

        catch (Exception exception) when (IsSessionException(exception))
        {
            StatusText.Text = $"Couldn't open the session. {exception.Message}";
        }

        finally
        {
            SetInteractionBlocked(false);
            RefreshState();
        }
    }

    private async Task<bool> ConfirmAsync(string title, string text, string action)
    {
        if (dialogOpen)
        {
            return false;
        }

        SetInteractionBlocked(true);

        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = text,
                PrimaryButtonText = action,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        finally
        {
            SetInteractionBlocked(false);
        }
    }

    private void SetInteractionBlocked(bool blocked)
    {
        dialogOpen = blocked;
        session.IsInteractionBlocked = blocked;
    }

    private void InitializePicker(object picker)
    {
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this)
        );
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closingAllowed)
        {
            return;
        }

        args.Cancel = true;

        if (
            session.HasChanges
            && !await ConfirmAsync(
                "Close this session?",
                "Your session has unsaved changes. Cancel to save it before closing.",
                "Discard and close"
            )
        )
        {
            return;
        }

        closingAllowed = true;
        Close();
    }

    private static bool IsSessionException(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or COMException;
    }
}
