using IgnitionSwitch.App.Audio;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession : IDisposable
{
    private readonly Lock inputLock = new();
    private readonly Lock feedbackLock = new();
    private readonly PerformanceInputDiagnostics inputDiagnostics = new();
    private readonly Dictionary<(int Channel, int Note), int> noteOwners = [];
    private readonly HashSet<(int Track, int Note)> previewOwners = [];
    private readonly int[] activeNoteOwners = new int[WorkstationEngine.TrackCount * 128];
    private readonly bool[] activePadFeedback = new bool[128];
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

    public event EventHandler<PerformanceActivity>? PerformanceChanged;
    public event EventHandler? PerformanceReset;

    public PerformanceInputDiagnostics.Snapshot ReadInputDiagnostics()
    {
        lock (inputLock)
        {
            return inputDiagnostics.Read();
        }
    }

    public void ResetInputDiagnostics()
    {
        lock (inputLock)
        {
            if (!disposed)
            {
                inputDiagnostics.Reset();
            }
        }
    }

    public void NoteOn(int track, int note, int velocity)
    {
        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            ValidatePerformanceNote(track, note);

            if (velocity == 0)
            {
                ReleasePreviewNote(track, note);
                return;
            }

            ForgetExpressionOwner(track, note);
            Engine.NoteOn(track, note, velocity, Audio.IsRunning);

            if (previewOwners.Add((track, note)))
            {
                activeNoteOwners[(track * 128) + note]++;
            }

            ReportPerformance(track, note, velocity, true);

            if (Engine.IsRecording)
            {
                revision++;
            }
        }
    }

    public void NoteOff(int track, int note)
    {
        lock (inputLock)
        {
            if (disposed)
            {
                return;
            }

            ValidatePerformanceNote(track, note);
            ReleasePreviewNote(track, note);
        }
    }

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
                knobPickup.Reset();

                if (value)
                {
                    StopCore();
                }
            }

            RefreshFeedback();
        }
    }

    public bool RecordingQuantizationEnabled
    {
        get
        {
            lock (inputLock)
            {
                return Engine.RecordingQuantizationEnabled;
            }
        }

        set
        {
            lock (inputLock)
            {
                if (!disposed && !interactionBlocked)
                {
                    Engine.RecordingQuantizationEnabled = value;
                }
            }
        }
    }

    public bool LoopEnabled
    {
        get
        {
            lock (inputLock)
            {
                return Engine.LoopEnabled;
            }
        }

        set
        {
            lock (inputLock)
            {
                if (disposed || interactionBlocked)
                {
                    return;
                }

                Engine.LoopEnabled = value;
            }

            RefreshFeedback();
        }
    }

    public void ToggleLoop()
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return;
            }

            Engine.LoopEnabled = !Engine.LoopEnabled;
        }

        RefreshFeedback();
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

            if (Engine.EndRecording())
            {
                revision++;
            }

            return (Engine.CaptureSession(), revision);
        }
    }

    public void ApplySession(SessionSnapshot snapshot)
    {
        lock (inputLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Engine.ApplySession(snapshot);
            soundDevice = SoundDeviceKind.Instrument;
            soundDeviceLocked = false;
            sessionRowMode = SessionRowMode.Clips;
            knobPickup.Reset();
            ClearPerformanceState();
            revision++;
        }

        RefreshFeedback();
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

    public bool Undo()
    {
        return ChangeHistory(false);
    }

    public bool Redo()
    {
        return ChangeHistory(true);
    }

    public bool QuantizeSelectedTrack()
    {
        bool changed;

        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return false;
            }

            changed = QuantizeSelectedTrackCore();
        }

        RefreshFeedback();
        return changed;
    }

    private bool QuantizeSelectedTrackCore()
    {
        bool wasRecording = Engine.IsRecording;
        bool changed = Engine.QuantizeSelectedTrack();

        if (changed || wasRecording)
        {
            revision++;
        }

        return changed;
    }

    private bool ChangeHistory(bool redo)
    {
        bool changed;

        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return false;
            }

            changed = ChangeHistoryCore(redo);
        }

        RefreshFeedback();
        return changed;
    }

    private bool ChangeHistoryCore(bool redo)
    {
        bool changed = redo ? Engine.Redo() : Engine.Undo();

        if (changed)
        {
            revision++;
        }

        return changed;
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
                ),
                Engine.LoopEnabled,
                CreateKnobFeedback(),
                soundDevice switch
                {
                    SoundDeviceKind.Delay => "Echo",
                    SoundDeviceKind.Reverb => "Room",
                    SoundDeviceKind.Instrument =>
                        tracks[soundDeviceLocked ? lockedSoundTrack : Engine.SelectedTrack].Name,
                    _ => tracks[soundDeviceLocked ? lockedSoundTrack : Engine.SelectedTrack].Name
                },
                soundDeviceLocked,
                CreateSessionFeedback()
            );

            if (
                previousFeedback is { } previous
                && previous.SelectedTrack == feedback.SelectedTrack
                && previous.Playing == feedback.Playing
                && previous.Recording == feedback.Recording
                && previous.LoopEnabled == feedback.LoopEnabled
                && previous.DeviceName == feedback.DeviceName
                && previous.DeviceLocked == feedback.DeviceLocked
                && ControllerSessionFeedback.HasSameState(previous.Session, feedback.Session)
                && previous.Knobs is not null
                    && previous.Knobs.SequenceEqual(feedback.Knobs!)
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
            ClearPerformanceState(true);
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
            knobPickup.Reset();

            if (Controller.IsConnected)
            {
                controlBank = ControllerControlBank.Device;
            }

            if (!Controller.IsConnected)
            {
                StopCore();
                Array.Clear(channelModulation);
            }
        }
    }

    private void OnInputReceived(object? sender, MidiInputEvent input)
    {
        lock (inputLock)
        {
            if (
                disposed
                || !Controller.IsConnected
                || !input.SourcePerformance
                || !IsValidPerformanceInput(input)
            )
            {
                return;
            }

            // Modal operations stop recording and block project controls, while live playing
            // remains direct and independent of the UI's focus or file-picker message loop.
            ProcessPerformance(input);
        }
    }

    private void OnControlReceived(object? sender, MusicControlEvent control)
    {
        lock (inputLock)
        {
            if (disposed || !Controller.IsConnected || !double.IsFinite(control.Value))
            {
                return;
            }

            if (
                control.Kind == MusicControlKind.SelectControlBank
                && Enum.IsDefined((ControllerControlBank)control.Index)
            )
            {
                // Surface mode is context, not a project mutation. Track it during a picker so
                // feedback and the first post-modal knob movement use the real hardware bank.
                SelectControlBankCore((ControllerControlBank)control.Index);
            }

            else if (!interactionBlocked)
            {
                ProcessControl(control);
            }
        }

        RefreshFeedback();
    }

    private void ProcessPerformance(MidiInputEvent input)
    {
        (int Channel, int Note) key = (input.Channel, input.Number);

        switch (input.Kind)
        {
            case MidiInputKind.NoteOn when input.Value > 0:
                int track = input.Channel == 9 ? 3 : Engine.SelectedTrack;
                _ = ReleaseNoteOwner(key);
                noteOwners[key] = track;
                activeNoteOwners[(track * 128) + input.Number]++;
                SustainTrackIfNeeded(track);
                BeginExpressiveNote(input.Channel, track, input.Number, input.Value);

                if (input.Channel == 9)
                {
                    inputDiagnostics.RecordDrumNoteOn(input.Value);
                }

                ReportPerformance(track, input.Number, input.Value, true);

                if (Engine.IsRecording)
                {
                    revision++;
                }

                break;

            case MidiInputKind.NoteOn:
            case MidiInputKind.NoteOff:
                if (ReleaseNoteOwner(key))
                {
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

            case MidiInputKind.ControlChange when input.Number == 1:
                SetChannelModulation(input.Channel, input.Value / 127.0);
                break;

            case MidiInputKind.ChannelAftertouch:
                SetChannelPressure(input.Channel, input.Value / 127.0);
                break;

            case MidiInputKind.PolyphonicAftertouch:
                SetPolyphonicPressure(input.Channel, input.Number, input.Value / 127.0);
                break;

            case MidiInputKind.ControlChange when input.Number is 120 or 123:
                Engine.AllNotesOff();
                ClearPerformanceState();

                if (Engine.IsRecording)
                {
                    revision++;
                }

                break;

            case MidiInputKind.ControlChange:
            case MidiInputKind.ProgramChange:
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
            case MusicControlKind.TriggerSessionPad:
                TriggerSessionPadCore(control.Index, control.Row);
                break;

            case MusicControlKind.MoveScene:
                MoveSceneCore(control.Index);
                break;

            case MusicControlKind.LaunchSelectedScene:
                if (Audio.IsRunning)
                {
                    Engine.QueueSceneLaunch(Engine.EditingSceneId);
                }

                break;

            case MusicControlKind.CycleSessionRowMode:
                sessionRowMode = (SessionRowMode)(((int)sessionRowMode + 1) % 4);
                break;

            case MusicControlKind.SetKnob:
                ProcessKnob(control);
                break;

            case MusicControlKind.SelectControlBank when Enum.IsDefined(
                (ControllerControlBank)control.Index
            ):
                SelectControlBankCore((ControllerControlBank)control.Index);
                break;

            case MusicControlKind.MoveSoundDevice:
                SelectSoundDeviceCore(
                    (SoundDeviceKind)Math.Clamp(
                        (int)soundDevice + Math.Clamp(control.Index, -1, 1),
                        0,
                        2
                    )
                );
                break;

            case MusicControlKind.SelectParameterPage when control.Index == 0:
                Engine.CommitEdit();
                break;

            case MusicControlKind.ToggleSoundDeviceLock:
                ToggleSoundDeviceLockCore();
                break;

            case MusicControlKind.CaptureRecentPerformance:
                _ = CaptureRecentPerformanceCore();
                break;

            case MusicControlKind.ToggleLoop:
                Engine.LoopEnabled = !Engine.LoopEnabled;
                break;

            case MusicControlKind.QuantizeSelectedTrack:
                _ = QuantizeSelectedTrackCore();
                break;

            case MusicControlKind.Undo:
                _ = ChangeHistoryCore(false);
                break;

            case MusicControlKind.Redo:
                _ = ChangeHistoryCore(true);
                break;

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
                _ = SetParameterCore(
                    new(ParameterId.Level, control.Index),
                    Math.Clamp(control.Value, 0, 1)
                );
                break;

            case MusicControlKind.SetMasterVolume:
                _ = SetParameterCore(
                    new(ParameterId.MasterVolume),
                    Math.Clamp(control.Value, 0, 1)
                );
                break;

            case MusicControlKind.SetTempo:
                _ = SetParameterCore(
                    new(ParameterId.Tempo),
                    (Math.Clamp(control.Value, 60, 180) - 60) / 120
                );
                break;

            case MusicControlKind.ToggleTrackMute:
            case MusicControlKind.SetTrackVolume:
            case MusicControlKind.SelectControlBank:
            case MusicControlKind.SelectParameterPage:
            default:
                break;
        }
    }

    private void SelectTrackCore(int track)
    {
        int selected = Math.Clamp(track, 0, WorkstationEngine.TrackCount - 1);

        if (selected != Engine.SelectedTrack)
        {
            Engine.CommitEdit();
            Engine.SelectedTrack = selected;
            knobPickup.Invalidate();
        }

        SustainTrackIfNeeded(Engine.SelectedTrack);
    }

    private void SustainTrackIfNeeded(int track)
    {
        if (
            pedalDown
            && !sustainedTracks.Contains(track)
            && Engine.Tracks[track].Instrument != InstrumentKind.Drums
        )
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
            ClearReleasedExpressionOwners();
        }
    }

    private void TogglePlaybackCore()
    {
        if (Engine.IsPlaying)
        {
            bool wasRecording = Engine.IsRecording;
            Engine.Pause();

            if (wasRecording)
            {
                revision++;
            }
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
        ClearPerformanceState();

        if (wasRecording)
        {
            revision++;
        }
    }

    private static bool IsTrack(int track)
    {
        return track is >= 0 and < WorkstationEngine.TrackCount;
    }

    private bool ReleaseNoteOwner((int Channel, int Note) key)
    {
        if (!noteOwners.Remove(key, out int track))
        {
            return false;
        }

        ReleaseExpressionOwner(track, key.Note, key.Channel);
        return ReleasePerformanceOwner(track, key.Note);
    }

    private bool ReleasePerformanceOwner(int track, int note)
    {
        int index = (track * 128) + note;

        if (--activeNoteOwners[index] != 0)
        {
            return false;
        }

        Engine.NoteOff(track, note);
        ReportPerformance(track, note, 0, false);
        return true;
    }

    private void ReleasePreviewNote(int track, int note)
    {
        if (
            previewOwners.Remove((track, note))
            && ReleasePerformanceOwner(track, note)
            && Engine.IsRecording
        )
        {
            revision++;
        }
    }

    private static void ValidatePerformanceNote(int track, int note)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, WorkstationEngine.TrackCount);
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 127);
    }

    private static bool IsValidPerformanceInput(MidiInputEvent input)
    {
        return input.Channel is >= 0
            and < 16 && input.Number is >= 0
            and <= 127
                && (input.Kind != MidiInputKind.ChannelAftertouch || input.Number == 0)
                && input.Value >= 0
                && input.Value <= (input.Kind == MidiInputKind.PitchBend ? 16383 : 127);
    }

    private void ClearPerformanceState(bool resetModulation = false)
    {
        for (int note = 0; note < activePadFeedback.Length; note++)
        {
            if (activePadFeedback[note])
            {
                Controller.UpdatePadFeedback(new PadFeedback(note, 0, false));
            }
        }

        noteOwners.Clear();
        ClearExpressionState(resetModulation);
        previewOwners.Clear();
        Array.Clear(activeNoteOwners);
        Array.Clear(activePadFeedback);
        sustainedTracks.Clear();
        pedalDown = false;
        PerformanceReset?.Invoke(this, EventArgs.Empty);
    }

    private void ReportPerformance(int track, int note, int velocity, bool pressed)
    {
        if (track == 3)
        {
            activePadFeedback[note] = pressed;
            Controller.UpdatePadFeedback(new PadFeedback(note, velocity, pressed));
        }

        PerformanceChanged?.Invoke(this, new PerformanceActivity(track, note, velocity, pressed));
    }
}
