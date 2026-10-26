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
    private readonly LoopTimeline timeline;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly List<TrackControls> trackControls = [];
    private bool updating;
    private bool closingAllowed;
    private bool dialogOpen;
    private bool disposed;
    private bool showingAudioError;
    private string timelineKey = string.Empty;
    private string sessionName = "Untitled";

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
        timeline = new LoopTimeline(TimelinePanel, index =>
        {
            session.SelectTrack(index);
            RefreshState();
        });
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
            TrackControls controls = InstrumentControls.CreateTrack(
                session.Engine.Tracks[index],
                index
            );
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
    }

    private void ResizeWorkspace()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        double scale = Content.XamlRoot.RasterizationScale;
        int width = Math.Min((int)(1360 * scale), display.WorkArea.Width);
        int height = Math.Min((int)(840 * scale), display.WorkArea.Height);
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
                || (key != VirtualKey.Escape
                    && FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or NumberBox)
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
        DeviceNameText.Text = session.Controller.Identity?.Model ?? "Devices";
        DeviceStatusDot.Fill = InstrumentControls.Brush(
            session.Controller.IsConnected ? "#6AD7E1" : "#9CA8BA"
        );
        SessionTitleText.Text = session.HasChanges ? $"{sessionName} · Unsaved" : sessionName;
        PlayButton.IsEnabled = session.Audio.IsRunning;
        RecordButton.IsEnabled = session.Audio.IsRunning;
        PlayIcon.Glyph = engine.IsPlaying ? "\uE769" : "\uE768";
        PlayLabel.Text = engine.IsPlaying ? "Pause" : "Play";
        RecordLabel.Text = engine.IsRecording ? "Recording" : "Record";
        TempoBox.Value = engine.Tempo;
        MasterSlider.Value = engine.MasterVolume;
        MetronomeToggle.IsChecked = engine.MetronomeEnabled;
        MuteButton.IsChecked = tracks[engine.SelectedTrack].IsMuted;
        SelectedTrackText.Text = tracks[engine.SelectedTrack].Name;
        LoopProgress.Value = engine.BeatPosition;
        timeline.UpdatePosition(engine.BeatPosition, engine.IsPlaying);
        PositionText.Text =
            $"Bar {(int)(engine.BeatPosition / 4) + 1} · beat {(int)(engine.BeatPosition % 4) + 1}";

        for (int index = 0; index < tracks.Count; index++)
        {
            TrackState track = tracks[index];
            TrackControls controls = trackControls[index];
            controls.Name.Text = track.Name;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                controls.Button,
                $"Select {track.Name}"
            );
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                controls.Volume,
                $"{track.Name} volume"
            );
            controls.Detail.Text =
                $"{track.NoteCount} {(track.NoteCount == 1 ? "note" : "notes")}{(track.IsMuted ? " · muted" : string.Empty)}";
            controls.Button.BorderBrush = InstrumentControls.Brush(
                index == engine.SelectedTrack ? track.ColorHex : "#30394A"
            );
            controls.Volume.Value = track.Volume;
        }

        Title = session.HasChanges ? "IgnitionSwitch · Unsaved session" : "IgnitionSwitch";
        string key =
            $"{session.Revision}:{engine.SelectedTrack}:{string.Join(',', tracks.Select(track => track.NoteCount))}";

        if (timelineKey != key)
        {
            timelineKey = key;
            DrawTimeline();
        }

        session.RefreshFeedback();

        if (!session.Audio.IsRunning)
        {
            StatusText.Text = session.Audio.Status;
            showingAudioError = true;
        }

        else if (showingAudioError)
        {
            StatusText.Text = string.Empty;
            showingAudioError = false;
        }

        StatusText.Visibility = string.IsNullOrEmpty(StatusText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        updating = false;
    }

    private void DrawTimeline()
    {
        timeline.Render(
            session.Engine.CaptureSession(),
            session.Engine.Tracks,
            session.Engine.SelectedTrack
        );
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
            StatusText.Text = "Track cleared.";
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
                SuggestedFileName = $"{sessionName}.ignition.json",
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
            SetSessionName(file.Name);
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
            SetSessionName(file.Name);
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

    private void SetSessionName(string filename)
    {
        const string extension = ".ignition.json";
        sessionName = filename.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? filename[..^extension.Length]
            : Path.GetFileNameWithoutExtension(filename);
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
