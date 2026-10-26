using IgnitionSwitch.App.Audio;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession : IDisposable
{
    private readonly Lock inputLock = new();
    private readonly Lock feedbackLock = new();
    private readonly Dictionary<(int Channel, int Note), int> noteOwners = [];
    private readonly HashSet<int> sustainedTracks = [];
    private ControllerFeedback? previousFeedback;
    private long revision;
    private long savedRevision;
    private bool pedalDown;
    private bool interactionBlocked;
    private bool disposed;

    public StudioSession(
        IMusicController controller,
        WorkstationEngine engine,
        IAudioPlayback audio
    )
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(audio);
        Controller = controller;
        Engine = engine;
        Audio = audio;
        Controller.InputReceived += OnInputReceived;
        Controller.ControlReceived += OnControlReceived;
        Controller.StatusChanged += OnControllerStatusChanged;
    }

    public WorkstationEngine Engine { get; }
    public IMusicController Controller { get; }
    public IAudioPlayback Audio { get; }

    public long Revision
    {
        get
        {
            lock (inputLock)
            {
                return revision;
            }
        }
    }

    public bool HasChanges
    {
        get
        {
            lock (inputLock)
            {
                return revision != savedRevision;
            }
        }
    }

    public bool IsInteractionBlocked
    {
        get
        {
            lock (inputLock)
            {
                return interactionBlocked;
            }
        }
        set
        {
            lock (inputLock)
            {
                if (disposed || interactionBlocked == value)
                {
                    return;
                }

                interactionBlocked = value;

                if (value)
                {
                    StopCore();
                }
            }

            RefreshFeedback();
        }
    }

    public void Start()
    {
        lock (inputLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Audio.Start();
            Controller.Connect();
        }
    }

    public void MarkChanged()
    {
        lock (inputLock)
        {
            if (!disposed)
            {
                revision++;
            }
        }
    }

    public void MarkSaved(long? revision = null)
    {
        lock (inputLock)
        {
            if (!disposed)
            {
                savedRevision = revision ?? this.revision;
            }
        }
    }

    public (SessionSnapshot Snapshot, long Revision) CaptureForSave()
    {
        lock (inputLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (Engine.IsRecording)
            {
                Engine.ToggleRecording();
                revision++;
            }

            return (Engine.CaptureSession(), revision);
        }
    }

    public void SelectTrack(int track)
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return;
            }

            SelectTrackCore(track);
        }

        RefreshFeedback();
    }

    public void TogglePlayback()
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked || !Audio.IsRunning)
            {
                return;
            }

            TogglePlaybackCore();
        }

        RefreshFeedback();
    }

    public void ToggleRecording()
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked || !Audio.IsRunning)
            {
                return;
            }

            Engine.ToggleRecording();
            revision++;
        }

        RefreshFeedback();
    }

    public void Stop()
    {
        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            StopCore();
        }

        RefreshFeedback();
    }

    public void RefreshFeedback()
    {
        lock (feedbackLock)
        {
            RefreshFeedbackCore();
        }
    }

    private void RefreshFeedbackCore()
    {
        ControllerFeedback feedback;

        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            IReadOnlyList<TrackState> tracks = Engine.Tracks;
            feedback = new(
                Engine.SelectedTrack,
                Engine.IsPlaying,
                Engine.IsRecording,
                Array.AsReadOnly(
                    tracks
                        .Select(track => track.Name)
                        .ToArray()
                ),
                Array.AsReadOnly(
                    tracks
                        .Select(track => track.IsMuted)
                        .ToArray()
                )
            );

            if (
                previousFeedback is { } previous
                && previous.SelectedTrack == feedback.SelectedTrack
                && previous.Playing == feedback.Playing
                && previous.Recording == feedback.Recording
                && previous.TrackNames.SequenceEqual(feedback.TrackNames)
                && previous.TrackMuted.SequenceEqual(feedback.TrackMuted)
            )
            {
                return;
            }

            previousFeedback = feedback;
        }

        // Hardware writes run outside the input lock so teardown never waits on a callback holding
        // it.
        try
        {
            Controller.UpdateFeedback(feedback);
        }

        catch (ObjectDisposedException) when (IsDisposed())
        {
            // A feedback update captured before shutdown can safely finish after disposal started.
        }
    }

    public void Dispose()
    {
        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            interactionBlocked = true;
            Controller.InputReceived -= OnInputReceived;
            Controller.ControlReceived -= OnControlReceived;
            Controller.StatusChanged -= OnControllerStatusChanged;
            noteOwners.Clear();
            sustainedTracks.Clear();
        }

        try
        {
            Controller.Dispose();
        }

        finally
        {
            try
            {
                Audio.Dispose();
            }

            finally
            {
                Engine.Dispose();
            }
        }
    }

    private bool IsDisposed()
    {
        lock (inputLock)
        {
            return disposed;
        }
    }

    private void OnControllerStatusChanged(object? sender, string status)
    {
        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            previousFeedback = null;

            if (!Controller.IsConnected)
            {
                StopCore();
            }
        }
    }

    private void OnInputReceived(object? sender, MidiInputEvent input)
    {
        lock (inputLock)
        {
            if (disposed || !input.SourcePerformance)
            {
                return;
            }

            bool release = input.Kind == MidiInputKind.NoteOff
                || input is { Kind: MidiInputKind.ControlChange, Number: 64, Value: < 64 }
                || input is { Kind: MidiInputKind.ControlChange, Number: 120 or 123 };

            if (!interactionBlocked || release)
            {
                ProcessPerformance(input);
            }
        }
    }

    private void OnControlReceived(object? sender, MusicControlEvent control)
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked || !double.IsFinite(control.Value))
            {
                return;
            }

            ProcessControl(control);
        }

        RefreshFeedback();
    }

    private void ProcessPerformance(MidiInputEvent input)
    {
        (int Channel, int Note) key = (input.Channel, input.Number);

        switch (input.Kind)
        {
            case MidiInputKind.NoteOn:
                int track = input.Channel == 9 ? 3 : Engine.SelectedTrack;

                if (noteOwners.Remove(key, out int oldTrack))
                {
                    Engine.NoteOff(oldTrack, input.Number);
                }

                noteOwners[key] = track;
                SustainTrackIfNeeded(track);
                Engine.NoteOn(track, input.Number, input.Value);

                if (Engine.IsRecording)
                {
                    revision++;
                }

                break;

            case MidiInputKind.NoteOff:
                if (noteOwners.Remove(key, out int owner))
                {
                    Engine.NoteOff(owner, input.Number);

                    if (Engine.IsRecording)
                    {
                        revision++;
                    }
                }

                break;

            case MidiInputKind.PitchBend:
                Engine.SetPitchBend((input.Value - 8192) / 8192.0);
                break;

            case MidiInputKind.ControlChange when input.Number == 64:
                SetPedal(input.Value >= 64);
                break;

            case MidiInputKind.ControlChange when input.Number is 120 or 123:
                Engine.AllNotesOff();
                noteOwners.Clear();
                sustainedTracks.Clear();
                pedalDown = false;

                if (Engine.IsRecording)
                {
                    revision++;
                }

                break;

            case MidiInputKind.ControlChange:
            case MidiInputKind.ProgramChange:
            case MidiInputKind.PolyphonicAftertouch:
            case MidiInputKind.ChannelAftertouch:
            case MidiInputKind.Clock:
            case MidiInputKind.Start:
            case MidiInputKind.Continue:
            case MidiInputKind.Stop:
            default:
                break;
        }
    }

    private void ProcessControl(MusicControlEvent control)
    {
        switch (control.Kind)
        {
            case MusicControlKind.TogglePlayback:
                if (Audio.IsRunning)
                {
                    TogglePlaybackCore();
                }

                break;

            case MusicControlKind.Stop:
                StopCore();
                break;

            case MusicControlKind.ToggleRecording:
                if (Audio.IsRunning)
                {
                    Engine.ToggleRecording();
                    revision++;
                }

                break;

            case MusicControlKind.ToggleMetronome:
                Engine.MetronomeEnabled = !Engine.MetronomeEnabled;
                break;

            case MusicControlKind.SelectTrack:
                SelectTrackCore(control.Index);
                break;

            case MusicControlKind.MoveTrack:
                SelectTrackCore(Engine.SelectedTrack + Math.Clamp(control.Index, -1, 1));
                break;

            case MusicControlKind.ToggleTrackMute when IsTrack(control.Index):
                Engine.SetTrackMuted(control.Index, !Engine.Tracks[control.Index].IsMuted);
                revision++;
                break;

            case MusicControlKind.SetTrackVolume when IsTrack(control.Index):
                Engine.SetTrackVolume(control.Index, (float)Math.Clamp(control.Value, 0, 1));
                revision++;
                break;

            case MusicControlKind.SetMasterVolume:
                Engine.MasterVolume = (float)Math.Clamp(control.Value, 0, 1);
                revision++;
                break;

            case MusicControlKind.SetTempo:
                Engine.Tempo = Math.Clamp(control.Value, 60, 180);
                revision++;
                break;

            case MusicControlKind.ToggleTrackMute:
            case MusicControlKind.SetTrackVolume:
            default:
                break;
        }
    }

    private void SelectTrackCore(int track)
    {
        Engine.SelectedTrack = Math.Clamp(track, 0, WorkstationEngine.TrackCount - 1);
        SustainTrackIfNeeded(Engine.SelectedTrack);
    }

    private void SustainTrackIfNeeded(int track)
    {
        if (pedalDown && Engine.Tracks[track].Instrument != InstrumentKind.Drums)
        {
            _ = sustainedTracks.Add(track);
            Engine.SetSustain(track, true);
        }
    }

    private void SetPedal(bool down)
    {
        pedalDown = down;

        if (down)
        {
            SustainTrackIfNeeded(Engine.SelectedTrack);

            foreach (int activeTrack in noteOwners.Values.Distinct())
            {
                SustainTrackIfNeeded(activeTrack);
            }
        }

        else
        {
            foreach (int track in sustainedTracks)
            {
                Engine.SetSustain(track, false);
            }

            if (Engine.IsRecording && sustainedTracks.Count > 0)
            {
                revision++;
            }

            sustainedTracks.Clear();
        }
    }

    private void TogglePlaybackCore()
    {
        if (Engine.IsPlaying)
        {
            StopCore();
        }

        else
        {
            Engine.Play();
        }
    }

    private void StopCore()
    {
        bool wasRecording = Engine.IsRecording;
        Engine.Stop();
        Engine.AllNotesOff();
        noteOwners.Clear();
        sustainedTracks.Clear();
        pedalDown = false;

        if (wasRecording)
        {
            revision++;
        }
    }

    private static bool IsTrack(int track)
    {
        return track is >= 0 and < WorkstationEngine.TrackCount;
    }
}
