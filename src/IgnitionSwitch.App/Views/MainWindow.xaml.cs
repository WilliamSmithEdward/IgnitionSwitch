using System.Runtime.InteropServices;
using System.Text.Json;
using IgnitionSwitch.App.Audio;
using IgnitionSwitch.App.Presentation;
using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;
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
    private readonly WorkspaceLayout workspaceLayout;
    private readonly SliderMotion masterMotion;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<TrackControls> trackControls = [];
    private readonly List<SliderMotion> trackMotions = [];
    private readonly PadActivityMailbox padActivity = new();
    private readonly long[] padSequences = new long[128];
    private readonly bool[] padPressed = new bool[128];
    private IReadOnlyList<PadControls> padControls = [];
    private CaptureResult? pendingCaptureResult;
    private bool updating;
    private bool closingAllowed;
    private bool dialogOpen;
    private bool disposed;
    private bool showingAudioError;
    private long timelineRevision = -1;
    private long lastStateRefresh;
    private double minimumWindowScale;
    private int mixerRefreshRequested;
    private int padResetRequested;
    private int stateRefreshRequested;
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
        masterMotion = new SliderMotion(MasterSlider);
        session.Controller.ControlReceived += OnControllerControlReceived;
        session.PerformanceChanged += OnPerformanceChanged;
        session.PerformanceReset += OnPerformanceReset;
        session.CaptureCompleted += OnCaptureCompleted;
        workspaceLayout = new WorkspaceLayout(
            WorkspaceScroll,
            WorkspaceRoot,
            WorkspaceBody,
            TrackColumn,
            EditorGrid,
            LoopRow,
            DrumsRow,
            TrackResizeHandle,
            LoopResizeHandle
        );
        WireControls();
        Content.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        timer.Tick += (_, _) =>
        {
            RefreshPadActivity();

            if (Interlocked.Exchange(ref mixerRefreshRequested, 0) != 0)
            {
                RefreshVolumes(session.Engine.Tracks);
            }

            timeline.UpdatePosition(session.Engine.BeatPosition, session.Engine.IsPlaying);

            if (
                Interlocked.Exchange(ref stateRefreshRequested, 0) != 0
                || Environment.TickCount64 - lastStateRefresh >= 100
            )
            {
                RefreshState();
            }
        };
        ((FrameworkElement)Content).Loaded += (_, _) =>
        {
            ResizeWorkspace();
            Content.XamlRoot.Changed += OnXamlRootChanged;
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

        foreach (PadControls pad in padControls)
        {
            pad.Dispose();
        }

        session.PerformanceChanged -= OnPerformanceChanged;
        session.PerformanceReset -= OnPerformanceReset;
        session.CaptureCompleted -= OnCaptureCompleted;
        session.Controller.ControlReceived -= OnControllerControlReceived;
        masterMotion.Dispose();

        foreach (SliderMotion motion in trackMotions)
        {
            motion.Dispose();
        }

        if (Content.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }

        workspaceLayout.Dispose();
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
            trackMotions.Add(new SliderMotion(controls.Volume));
            WireEditGesture(controls.Volume);
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

        ControllerPadLayout? layout = session.Controller.DrumPadLayout;
        padControls = InstrumentControls.CreatePads(
            PadPanel,
            PreviewNoteOn,
            PreviewNoteOff,
            layout?.Notes,
            layout?.Columns ?? 8
        );
    }

    private void ResizeWorkspace()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        double scale = Content.XamlRoot.RasterizationScale;
        SetMinimumWindowSize(display, scale);
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

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (minimumWindowScale != sender.RasterizationScale)
        {
            SetMinimumWindowSize(
                DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest),
                sender.RasterizationScale
            );
        }
    }

    private void SetMinimumWindowSize(DisplayArea display, double scale)
    {
        minimumWindowScale = scale;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(
                (int)Math.Ceiling(960 * scale),
                display.WorkArea.Width
            );
            presenter.PreferredMinimumHeight = Math.Min(
                (int)Math.Ceiling(600 * scale),
                display.WorkArea.Height
            );
        }
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
        WireEditGesture(MasterSlider);
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
        RepeatLoopItem.Click += (_, _) =>
        {
            if (!updating)
            {
                session.LoopEnabled = RepeatLoopItem.IsChecked;
                RefreshState();
            }
        };
        QuantizeTrackItem.Click += (_, _) =>
        {
            _ = session.QuantizeSelectedTrack();
            RefreshState();
        };
        CaptureRecentItem.Click += (_, _) =>
        {
            _ = session.CaptureRecentPerformance();
            RefreshState();
        };
        RecordingGridItem.Click += (_, _) =>
        {
            if (!updating)
            {
                session.RecordingQuantizationEnabled = RecordingGridItem.IsChecked;
                RefreshState();
            }
        };
        ClearButton.Click += async (_, _) => await ClearTrackAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        OpenButton.Click += async (_, _) => await OpenAsync();
        AddShortcut(VirtualKey.Space, session.TogglePlayback);
        AddShortcut(VirtualKey.R, session.ToggleRecording);
        AddShortcut(VirtualKey.Escape, session.Stop);
        AddShortcut(
            VirtualKey.Z,
            () =>
            {
                _ = session.Undo();
            },
            VirtualKeyModifiers.Control
        );
        AddShortcut(
            VirtualKey.Z,
            () =>
            {
                _ = session.Redo();
            },
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
        );
    }

    private void WireEditGesture(Slider slider)
    {
        slider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => CompleteEdit()),
            true
        );
        slider.PointerCaptureLost += (_, _) => CompleteEdit();
        slider.KeyUp += (_, args) =>
        {
            if (
                args.Key is VirtualKey.Left
                    or VirtualKey.Right
                    or VirtualKey.Up
                    or VirtualKey.Down
                    or VirtualKey.Home
                    or VirtualKey.End
                    or VirtualKey.PageUp
                    or VirtualKey.PageDown
            )
            {
                CompleteEdit();
            }
        };
    }

    private void CompleteEdit()
    {
        if (!disposed)
        {
            session.Engine.CommitEdit();
        }
    }

    private void AddShortcut(
        VirtualKey key,
        Action action,
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None
    )
    {
        var shortcut = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers
        };
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
        session.NoteOn(track, note, 100);
    }

    private void PreviewNoteOff(int track, int note)
    {
        session.NoteOff(track, note);
    }

    private void OnPerformanceChanged(object? sender, PerformanceActivity activity)
    {
        if (activity.Track == 3)
        {
            padActivity.Record(activity.Note, activity.Velocity, activity.IsPressed);
        }
    }

    private void OnPerformanceReset(object? sender, EventArgs args)
    {
        padActivity.Clear();
        _ = Interlocked.Exchange(ref pendingCaptureResult, null);
        _ = Interlocked.Exchange(ref padResetRequested, 1);
    }

    private void OnCaptureCompleted(object? sender, CaptureResult result)
    {
        _ = Interlocked.Exchange(ref pendingCaptureResult, result);
        _ = Interlocked.Exchange(ref stateRefreshRequested, 1);
    }

    private void RefreshPadActivity()
    {
        if (Interlocked.Exchange(ref padResetRequested, 0) != 0)
        {
            Array.Clear(padSequences);
            Array.Clear(padPressed);

            foreach (PadControls pad in padControls)
            {
                pad.Release();
            }
        }

        foreach (PadControls pad in padControls)
        {
            PadActivity state = padActivity.Read(pad.Note);

            if (state.Velocity > 0 && state.Sequence != padSequences[pad.Note])
            {
                pad.Hit(state.Velocity);
            }

            if (
                !state.IsPressed
                && (padPressed[pad.Note] || state.Sequence != padSequences[pad.Note])
            )
            {
                pad.Release();
            }

            padSequences[pad.Note] = state.Sequence;
            padPressed[pad.Note] = state.IsPressed;
        }
    }

    private void RefreshState()
    {
        lastStateRefresh = Environment.TickCount64;
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
        RefreshVolumes(tracks);
        MetronomeToggle.IsChecked = engine.MetronomeEnabled;
        MuteButton.IsChecked = tracks[engine.SelectedTrack].IsMuted;
        LoopOptionsButton.IsEnabled = !session.IsInteractionBlocked;
        RepeatLoopItem.IsChecked = session.LoopEnabled;
        LoopLengthText.Text = engine.LoopEnabled
            ? $"{engine.LoopBars} bars"
            : $"{engine.LoopBars} bars · once";
        RepeatLoopItem.IsEnabled = !session.IsInteractionBlocked;
        QuantizeTrackItem.IsEnabled = !session.IsInteractionBlocked
            && (engine.IsRecording || tracks[engine.SelectedTrack].NoteCount > 0);
        CaptureRecentItem.IsEnabled = session.CanCaptureRecentPerformance;
        RecordingGridItem.IsChecked = session.RecordingQuantizationEnabled;
        RecordingGridItem.IsEnabled = !session.IsInteractionBlocked;
        SelectedTrackText.Text = tracks[engine.SelectedTrack].Name;
        double beatPosition = engine.BeatPosition;
        LoopProgress.Value = beatPosition;
        timeline.UpdatePosition(beatPosition, engine.IsPlaying);
        PositionText.Text = beatPosition >= engine.LoopBeats
            ? "End"
            : $"Bar {(int)(beatPosition / 4) + 1} · beat {(int)(beatPosition % 4) + 1}";

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
        }

        Title = session.HasChanges ? "IgnitionSwitch · Unsaved session" : "IgnitionSwitch";

        if (timelineRevision != engine.NoteRevision)
        {
            timelineRevision = engine.NoteRevision;
            DrawTimeline();
        }

        else
        {
            timeline.UpdateTracks(tracks, engine.SelectedTrack);
        }

        session.RefreshFeedback();

        if (!session.Audio.IsRunning)
        {
            StatusText.Text = session.Audio.Status;
            showingAudioError = true;
        }

        else if (Interlocked.Exchange(ref pendingCaptureResult, null) is { } captureResult)
        {
            StatusText.Text = CaptureStatusText(captureResult);
            showingAudioError = false;
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

    private static string CaptureStatusText(CaptureResult result)
    {
        return result.Status switch
        {
            CaptureStatus.Captured =>
                $"{result.NoteCount} {(result.NoteCount == 1 ? "note" : "notes")} captured.",
            CaptureStatus.NoChanges => "Recent playing is already in the loop.",
            CaptureStatus.NoPerformance => "Play something first.",
            CaptureStatus.CapacityExceeded =>
                "Capture needs more room. Clear a track or start a new session.",
            CaptureStatus.HistoryTruncated =>
                "That phrase exceeded the capture buffer. Play a shorter phrase.",
            _ => string.Empty
        };
    }

    private void OnControllerControlReceived(object? sender, MusicControlEvent control)
    {
        if (control.Kind is MusicControlKind.SetTrackVolume or MusicControlKind.SetMasterVolume)
        {
            _ = Interlocked.Exchange(ref mixerRefreshRequested, 1);
            return;
        }

        _ = Interlocked.Exchange(ref stateRefreshRequested, 1);
    }

    private void RefreshVolumes(IReadOnlyList<TrackState> tracks)
    {
        bool previous = updating;
        updating = true;

        try
        {
            masterMotion.SetValue(session.Engine.MasterVolume);

            for (int index = 0; index < tracks.Count; index++)
            {
                trackMotions[index].SetValue(tracks[index].Volume);
            }
        }

        finally
        {
            updating = previous;
        }
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
            timelineRevision = -1;
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
            session.ApplySession(snapshot);
            session.MarkSaved();
            SetSessionName(file.Name);
            timelineRevision = -1;
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
