using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Audio.Effects;
using IgnitionSwitch.Core.Audio.Mixing;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;

namespace IgnitionSwitch.Core.Sequencing;

/// <summary>A thread-safe, sample-clocked four-track synthesizer and scene-based clip recorder.</summary>
public sealed partial class WorkstationEngine : IDisposable
{
    /// <summary>The fixed rendering sample rate in frames per second.</summary>
    public const int SampleRate = 48000;

    /// <summary>The fixed number of instrument and mixer lanes.</summary>
    public const int TrackCount = 4;

    /// <summary>The maximum number of recorded MIDI notes stored in one track clip.</summary>
    /// <remarks>The legacy API name applies separately to each track/scene clip. Combined content is bounded by <see cref="MaximumTotalNotes"/>.</remarks>
    public const int MaximumNotesPerTrack = 2048;

    /// <summary>The maximum number of scenes in a project.</summary>
    public const int MaximumScenes = 8;

    /// <summary>The combined note budget across all clips and scenes.</summary>
    public const int MaximumTotalNotes = TrackCount * MaximumNotesPerTrack;
    private const double QuantizeBeats = 0.25;
    private const int MaximumVoices = 64;
    private const int RenderQuantumFrames = 64;
    private const int MaximumProjectNotes = MaximumTotalNotes;
    private readonly Lock _sync = new();
    private readonly List<SynthVoice> _voices = new(MaximumVoices);
    private readonly Dictionary<(int Track, int Note), HeldNote> _held = new(TrackCount * 128);
    private readonly Dictionary<int, PendingLoopStart> _pendingLoopStarts = new(
        MaximumProjectNotes
    );
    private readonly Track[] _tracks =
    [
        new("Velvet keys", InstrumentKind.Keys, "#9B8CFF"),
        new("Round bass", InstrumentKind.Bass, "#5CD7C4"),
        new("Soft atmosphere", InstrumentKind.Atmosphere, "#E7AF68"),
        new("Pocket drums", InstrumentKind.Drums, "#F279A2")
    ];
    private bool _isPlaying;
    private bool _isRecording;
    private bool _metronomeEnabled;
    private bool _disposed;
    private double _tempo = 100;
    private float _masterVolume = 0.65f;
    private int _selectedTrack;
    private double _absoluteBeat;
    private double _pitchRatio = 1;
    private int _nextVoiceId;
    private long _lastClickBeat = -1;
    private int _clickSamples;
    private double _clickFrequency;
    private long _noteRevision;

    /// <summary>Gets the four-bar loop length.</summary>
    public int LoopBars { get; } = 4;

    /// <summary>Gets the loop length in quarter-note beats.</summary>
    public double LoopBeats => LoopBars * 4;

    /// <summary>Gets or sets tempo from 60 to 180 beats per minute.</summary>
    public double Tempo
    {
        get
        {
            lock (_sync)
            {
                return _tempo;
            }
        }
        set
        {
            ValidateRange(value, 60, 180, nameof(value));

            lock (_sync)
            {
                ThrowIfDisposed();
                SetStoredParameter(new(ParameterId.Tempo), value);
            }
        }
    }

    /// <summary>Gets whether the loop transport is running.</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _isPlaying;
            }
        }
    }

    /// <summary>Gets whether new MIDI notes are being overdubbed into the loop.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _isRecording;
            }
        }
    }

    /// <summary>Gets or sets whether to sound the sample-synchronized metronome during playback.</summary>
    public bool MetronomeEnabled
    {
        get
        {
            lock (_sync)
            {
                return _metronomeEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _metronomeEnabled = value;
            }
        }
    }

    /// <summary>Gets or sets the zero-based track targeted by selected-track operations.</summary>
    public int SelectedTrack
    {
        get
        {
            lock (_sync)
            {
                return _selectedTrack;
            }
        }
        set
        {
            ValidateTrack(value);

            lock (_sync)
            {
                ThrowIfDisposed();
                _selectedTrack = value;
            }
        }
    }

    /// <summary>Gets or sets master gain from zero to one, before the output limiter.</summary>
    public float MasterVolume
    {
        get
        {
            lock (_sync)
            {
                return _masterVolume;
            }
        }
        set
        {
            ValidateRange(value, 0, 1, nameof(value));

            lock (_sync)
            {
                ThrowIfDisposed();
                SetStoredParameter(new(ParameterId.MasterVolume), value);
            }
        }
    }

    /// <summary>Gets the audio-clock position within the loop, retaining sixteen beats when a non-repeating pass completes.</summary>
    public double BeatPosition
    {
        get
        {
            lock (_sync)
            {
                return _passCompleted ? LoopBeats : _absoluteBeat % LoopBeats;
            }
        }
    }

    /// <summary>Gets a revision for recorded note changes and scene metadata or structure changes. Mixer targets and transient playback choices do not change it.</summary>
    public long NoteRevision
    {
        get
        {
            lock (_sync)
            {
                return _noteRevision;
            }
        }
    }

    /// <summary>Gets detached, read-only track state snapshots in display order, with note counts for the editing scene.</summary>
    public IReadOnlyList<TrackState> Tracks
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(
                    _tracks
                        .Select(
                            track => new TrackState(
                                track.Name,
                                track.Instrument,
                                track.ColorHex,
                                track.Clips[_editingSceneId].Notes.Count,
                                track.IsMuted,
                                track.Volume
                            )
                        )
                        .ToArray()
                );
            }
        }
    }

    /// <summary>Starts or resumes loop playback; live instruments are playable while paused or stopped.</summary>
    public void Play()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _history.Commit();
            StartPlayback();
        }
    }

    /// <summary>Ends recording and silences sequenced voices while preserving the transport position.</summary>
    /// <remarks>
    /// Held live notes and sustain remain playable. Resume does not restart interrupted sequenced notes;
    /// they retrigger at their next scheduled loop onset, including notes spanning the loop boundary.
    /// </remarks>
    public void Pause()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            EndSequencedPlayback();
        }
    }

    /// <summary>Ends recording, silences voices, resets sustain, and rewinds the transport.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            EndSequencedPlayback();
            _performance.CloseAll();
            ResetPlaybackPosition();
            _held.Clear();
            _voices.Clear();
            ResetEffects();

            foreach (Track track in _tracks)
            {
                track.Sustain = false;
                track.ActiveSceneId = null;
            }

            _launchRevision++;
        }
    }

    /// <summary>Pauses running playback or resumes it from the preserved transport position.</summary>
    public void TogglePlayback()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_isPlaying)
            {
                Pause();
            }

            else
            {
                Play();
            }
        }
    }

    /// <summary>Toggles overdubbing using the current recording-quantization policy, starting playback when necessary.</summary>
    public void ToggleRecording()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_isRecording)
            {
                _ = EndRecordingCore();
            }

            else
            {
                _history.Commit();
                StartPlayback();
                _isRecording = true;
                ArmStoppedRecordingLanes();
            }
        }
    }

    /// <summary>Plays a MIDI note on the selected track; velocity zero releases the note.</summary>
    public void NoteOn(int note, int velocity)
    {
        NoteOn(SelectedTrack, note, velocity);
    }

    /// <summary>Plays a MIDI note on a specific track independently of the selected track.</summary>
    public void NoteOn(int track, int note, int velocity)
    {
        NoteOn(track, note, velocity, true);
    }

    /// <summary>Plays a live note and optionally retains its clocked performance for retrospective capture; false excludes unavailable-audio input from that history.</summary>
    /// <remarks>At most 64 voices sound at once. Sequenced voices are replaced before live voices; fresh live attacks retain their first 4 ms against sequenced replacement. More than 64 simultaneous live attacks still replace the oldest live voice.</remarks>
    public void NoteOn(int track, int note, int velocity, bool retainForCapture)
    {
        ValidateTrack(track);
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 127);
        ArgumentOutOfRangeException.ThrowIfNegative(velocity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(velocity, 127);

        if (velocity == 0)
        {
            NoteOff(track, note);
            return;
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_held.Remove((track, note), out HeldNote? previous))
            {
                ReleaseHeld(previous);
            }

            int id = NextVoiceId();
            AddVoice(CreateVoice(id, track, note, velocity, false));
            _performance.NoteOn(
                track,
                note,
                velocity,
                retainForCapture,
                _recordingQuantizationEnabled,
                _isPlaying,
                _absoluteBeat,
                _tempo / (60 * SampleRate)
            );
            HeldNote held = new(
                id,
                track,
                note,
                velocity,
                _isRecording && _tracks[track].ActiveSceneId is not null ? _absoluteBeat : null,
                _recordingQuantizationEnabled,
                _tracks[track].ActiveSceneId ?? _editingSceneId,
                _tracks[track].Origin
            );

            if (_tracks[track].Instrument == InstrumentKind.Drums)
            {
                if (_isRecording)
                {
                    RecordNote(held, QuantizeBeats);
                }
            }

            else
            {
                _held[(track, note)] = held;
            }
        }
    }

    /// <summary>Releases a live MIDI note on every track, including notes held before selection changed.</summary>
    public void NoteOff(int note)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 127);

        lock (_sync)
        {
            ThrowIfDisposed();

            for (int track = 0; track < TrackCount; track++)
            {
                ReleaseKey(track, note);
            }
        }
    }

    /// <summary>Releases a live MIDI note on a specific track, subject to its sustain state.</summary>
    public void NoteOff(int track, int note)
    {
        ValidateTrack(track);
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 127);

        lock (_sync)
        {
            ThrowIfDisposed();
            ReleaseKey(track, note);
        }
    }

    /// <summary>Sets sustain on the selected track.</summary>
    public void SetSustain(bool enabled)
    {
        SetSustain(SelectedTrack, enabled);
    }

    /// <summary>Sets sustain on a specific track and releases deferred notes when disabled.</summary>
    public void SetSustain(int track, bool enabled)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
            _tracks[track].Sustain = enabled;

            if (!enabled)
            {
                foreach ((int heldTrack, int note) in _held.Keys
                    .Where(key => key.Track == track)
                    .ToArray())
                {
                    HeldNote held = _held[(heldTrack, note)];

                    if (!held.IsKeyDown)
                    {
                        _ = _held.Remove((heldTrack, note));
                        ReleaseHeld(held);
                    }
                }
            }
        }
    }

    /// <summary>Sets melodic pitch bend from negative one to one, corresponding to minus or plus two semitones.</summary>
    public void SetPitchBend(double normalized)
    {
        ValidateRange(normalized, -1, 1, nameof(normalized));

        lock (_sync)
        {
            ThrowIfDisposed();
            _pitchRatio = Math.Pow(2, normalized * 2 / 12);
        }
    }

    /// <summary>Immediately silences live and loop voices and resets sustain and pitch bend.</summary>
    public void AllNotesOff()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            FinalizeHeldRecordings();
            _history.Commit();
            _performance.CloseAll();
            _held.Clear();
            _voices.Clear();
            ResetEffects();
            _pitchRatio = 1;

            foreach (Track track in _tracks)
            {
                track.Sustain = false;
            }
        }
    }

    /// <summary>Clears the selected track clip in the editing scene without changing its instrument or mixer state.</summary>
    public void ClearSelectedTrack()
    {
        lock (_sync)
        {
            _ = ClearClip(new(_selectedTrack, _editingSceneId));
        }
    }

    /// <summary>Clears one exact clip as an undoable edit, preserving selection, live voices, and unrelated clips. Returns false when already empty.</summary>
    public bool ClearClip(ClipAddress address)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateAddress(address);
            Clip selected = _tracks[address.Track].Clips[address.SceneId];

            if (selected.Notes.Count == 0)
            {
                return false;
            }

            _history.Commit();

            for (int index = selected.Notes.Count - 1; index >= 0; index--)
            {
                StoredNote stored = selected.Notes[index];
                _history.RecordNote(
                    new(address.Track, stored.Id, index, stored.Note, null, address.SceneId)
                );
                _ = _pendingLoopStarts.Remove(stored.Id);
            }

            RemoveClipVoices(address);
            selected.Notes.Clear();
            selected.Schedule.Clear();
            selected.Cursor = 0;

            foreach (HeldNote held in _held.Values)
            {
                if (held.Track == address.Track && held.RecordSceneId == address.SceneId)
                {
                    held.RecordStart = null;
                }
            }

            _noteRevision++;
            _history.Commit();
            return true;
        }
    }

    /// <summary>Mutes or unmutes a specific track.</summary>
    public void SetTrackMuted(int track, bool muted)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
            SetStoredParameter(new(ParameterId.TrackMute, track), muted ? 1 : 0);
        }
    }

    /// <summary>Sets a specific track's gain from zero to one.</summary>
    public void SetTrackVolume(int track, float volume)
    {
        ValidateTrack(track);
        ValidateRange(volume, 0, 1, nameof(volume));

        lock (_sync)
        {
            ThrowIfDisposed();
            SetStoredParameter(new(ParameterId.Level, track), volume);
        }
    }

    /// <summary>Fills an even number of interleaved stereo floating-point samples at 48 kHz.</summary>
    /// <remarks>The transport advances only here; output is finite and limited to minus one through one.</remarks>
    public void Render(float[] buffer, int offset, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Span<float> samples = buffer.AsSpan(offset, sampleCount);

        if (sampleCount % 2 != 0)
        {
            throw new ArgumentException(
                "Stereo rendering requires an even sample count.",
                nameof(sampleCount)
            );
        }

        int sample = 0;

        do
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                double beatsPerSample = _tempo / (60 * SampleRate);
                int quantumEnd = Math.Min(samples.Length, sample + (RenderQuantumFrames * 2));

                for (; sample < quantumEnd; sample += 2)
                {
                    if (_isPlaying)
                    {
                        ApplyDueLaunches();
                        RenderScheduledFrame();

                        long wholeBeat = (long)Math.Floor(_absoluteBeat + 1e-9);

                        if (wholeBeat != _lastClickBeat)
                        {
                            _lastClickBeat = wholeBeat;

                            if (_metronomeEnabled)
                            {
                                _clickSamples = SampleRate / 40;
                                _clickFrequency = wholeBeat % 4 == 0 ? 1400 : 1000;
                            }
                        }
                    }

                    StereoSample mixed = RenderMixedFrame();
                    float click = 0;

                    if (_clickSamples > 0)
                    {
                        click =
                            (float)(Math.Sin(
                                2 * Math.PI * _clickFrequency * _clickSamples / SampleRate
                            )
                                * _clickSamples
                                / (SampleRate / 40.0)
                                * 0.08);
                        _clickSamples--;
                    }

                    float master = _masterGain.Next();
                    float left = (mixed.Left + click) * master;
                    float right = (mixed.Right + click) * master;
                    samples[sample] = MathF.Tanh(left);
                    samples[sample + 1] = MathF.Tanh(right);
                    _performance.Advance(beatsPerSample);

                    if (_isPlaying)
                    {
                        _absoluteBeat += beatsPerSample;

                        if (
                            _finishAtBeat is double end
                            && _absoluteBeat >= end - BeatBoundaryTolerance
                        )
                        {
                            _absoluteBeat = end;
                            _passCompleted = true;
                            _finishAtBeat = null;
                            EndSequencedPlayback();
                            ClearLaunchQueues();
                            _resumeExistingMix = false;
                        }
                    }
                }
            }
        }

        while (sample < samples.Length);
    }

    /// <summary>Captures completed recorded notes and mixer state in a detached, read-only project snapshot.</summary>
    public SessionSnapshot CaptureSession()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var tracks = new TrackSnapshot[TrackCount];

            for (int track = 0; track < TrackCount; track++)
            {
                Track source = _tracks[track];
                ClipSnapshot[] clips =
                [
                    .. _scenes.Select(
                        scene => new ClipSnapshot(
                            scene.Id,
                            Array.AsReadOnly(
                                source.Clips[scene.Id].Notes
                                    .Select(note => note.Note)
                                    .ToArray()
                            )
                        )
                    )
                ];
                tracks[track] = new(
                    source.Name,
                    source.Instrument,
                    source.IsMuted,
                    source.Volume,
                    [],
                    source.Sound.Snapshot(),
                    Array.AsReadOnly(clips)
                );
            }

            return new(
                3,
                _tempo,
                LoopBars,
                _masterVolume,
                Array.AsReadOnly(tracks),
                _effects.DelaySnapshot(),
                _effects.ReverbSnapshot(),
                Array.AsReadOnly(_scenes.ToArray())
            );
        }
    }

    /// <summary>Validates and clones a project before atomically replacing the stopped engine state.</summary>
    public void ApplySession(SessionSnapshot session)
    {
        session = SessionStore.ValidateSession(session);

        lock (_sync)
        {
            ThrowIfDisposed();
            Stop();
            _scenes.Clear();
            _scenes.AddRange(session.Scenes!);
            _editingSceneId = _scenes[0].Id;
            _tempo = session.Tempo;
            _masterVolume = session.MasterVolume;
            _masterGain.SetTarget(_masterVolume, true);
            _effects.Apply(session.Delay!, session.Reverb!);

            for (int track = 0; track < TrackCount; track++)
            {
                TrackSnapshot source = session.Tracks[track];
                Track target = _tracks[track];
                target.Name = source.Name;
                target.Instrument = source.Instrument;
                target.Sound = new(source.Instrument);
                target.Sound.Apply(source.Sound!, source.Volume);
                target.IsMuted = source.IsMuted;
                target.IsSolo = false;
                target.Volume = source.Volume;
                target.EditSceneId = _editingSceneId;
                target.ActiveSceneId = _editingSceneId;

                foreach (Clip clip in target.Clips)
                {
                    clip.Notes.Clear();
                }

                foreach (ClipSnapshot clip in source.Clips)
                {
                    foreach (LoopNote note in clip.Notes)
                    {
                        target.Clips[clip.SceneId].Notes.Add(
                            new StoredNote(NextVoiceId(), note, clip.SceneId)
                        );
                    }
                }
            }

            RebuildSchedule();
            _noteRevision++;
            _launchRevision++;
            _history.Reset();
            _performance.Clear();
            _captureWorkspace?.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _voices.Clear();
            _held.Clear();

            foreach (Track track in _tracks)
            {
                foreach (Clip clip in track.Clips)
                {
                    clip.Schedule.Clear();
                }
            }

            _pendingLoopStarts.Clear();
            _history.Reset();
            _performance.Clear();
            _captureWorkspace?.Clear();
            _isPlaying = false;
            _isRecording = false;
            _disposed = true;
        }
    }

    private static void ValidateTrack(int track)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, TrackCount);
    }

    private static void ValidateRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Expected a finite number from {minimum} to {maximum}."
            );
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private int NextVoiceId()
    {
        _nextVoiceId = _nextVoiceId == int.MaxValue ? 1 : _nextVoiceId + 1;
        return _nextVoiceId;
    }

    private void AddVoice(SynthVoice voice)
    {
        if (_voices.Count >= MaximumVoices)
        {
            int loop = -1;
            int unprotectedLive = -1;

            for (int index = 0; index < _voices.Count; index++)
            {
                SynthVoice existing = _voices[index];

                if (existing.IsLoop)
                {
                    loop = index;
                    break;
                }

                if (unprotectedLive < 0 && !existing.IsProtectedLiveAttack)
                {
                    unprotectedLive = index;
                }
            }

            int victim = loop >= 0
                ? loop
                : unprotectedLive >= 0 ? unprotectedLive : voice.IsLoop ? -1 : 0;

            if (victim < 0)
            {
                return;
            }

            // More than 64 simultaneous live attacks still require oldest-live replacement.
            _voices.RemoveAt(victim);
        }

        _voices.Add(voice);
    }

    private void ReleaseKey(int track, int note)
    {
        if (_tracks[track].Instrument == InstrumentKind.Drums)
        {
            _performance.CloseNote(track, note);
        }

        if (!_held.TryGetValue((track, note), out HeldNote? held))
        {
            return;
        }

        held.IsKeyDown = false;

        if (!_tracks[track].Sustain)
        {
            _ = _held.Remove((track, note));
            ReleaseHeld(held);
        }
    }

    private void ReleaseHeld(HeldNote held)
    {
        _performance.CloseNote(held.Track, held.Note);

        foreach (SynthVoice voice in _voices)
        {
            if (voice.Id == held.Id)
            {
                voice.Release(_tracks[held.Track].Sound.ReleasePhysical);
            }
        }

        if (held.RecordStart is double start)
        {
            RecordNote(held, _absoluteBeat - start);
            held.RecordStart = null;
        }
    }

    private void FinalizeHeldRecordings()
    {
        if (_held.Count == 0)
        {
            return;
        }

        int recordingCount = 0;

        foreach (HeldNote held in _held.Values)
        {
            if (held.RecordStart is not null)
            {
                recordingCount++;
            }
        }

        if (recordingCount == 0)
        {
            return;
        }

        bool updateSchedule = recordingCount == 1;
        long revision = _noteRevision;

        foreach (HeldNote held in _held.Values)
        {
            if (held.RecordStart is double start)
            {
                RecordNote(held, _absoluteBeat - start, updateSchedule);
                held.RecordStart = null;
            }
        }

        if (!updateSchedule && _noteRevision != revision)
        {
            RebuildSchedule();
            AlignHistorySchedule();
        }
    }

    private void RecordNote(HeldNote held, double duration, bool updateSchedule = true)
    {
        if (held.RecordStart is not double start)
        {
            return;
        }

        double localStart = start - held.RecordOrigin;
        double beat = held.QuantizeRecording ? QuantizeOnset(localStart) : WrapClipBeat(localStart);
        duration = held.QuantizeRecording
            ? Math.Clamp(RoundToQuantizationGrid(duration), QuantizeBeats, LoopBeats)
            : Math.Clamp(duration, _tempo / (60 * SampleRate), LoopBeats);
        Clip track = _tracks[held.Track].Clips[held.RecordSceneId];
        StoredNote? replacement = null;
        int replacementIndex = -1;
        bool removedDuplicate = false;

        for (int index = track.Notes.Count - 1; index >= 0; index--)
        {
            StoredNote stored = track.Notes[index];

            if (stored.Note.Note != held.Note || stored.Note.Beat != beat)
            {
                continue;
            }

            ReleaseLoopVoices(stored.Id);

            if (updateSchedule)
            {
                RemoveScheduledNote(stored);
            }

            _ = _pendingLoopStarts.Remove(stored.Id);

            if (replacement is null)
            {
                replacement = stored;
                replacementIndex = index;
            }

            else
            {
                _history.RecordNote(
                    new(held.Track, stored.Id, index, stored.Note, null, held.RecordSceneId)
                );
                track.Notes.RemoveAt(index);
                removedDuplicate = true;
            }
        }

        if (
            replacement is null
                && (track.Notes.Count >= MaximumNotesPerTrack
                    || TotalNoteCount() >= MaximumProjectNotes)
        )
        {
            return;
        }

        LoopNote recorded = new(held.Note, held.Velocity, beat, duration);
        bool changed = replacement is null || replacement.Note != recorded || removedDuplicate;

        if (replacement is null)
        {
            replacement = new StoredNote(NextVoiceId(), recorded, held.RecordSceneId);
            track.Notes.Add(replacement);
            _history.RecordNote(
                new(
                    held.Track,
                    replacement.Id,
                    track.Notes.Count - 1,
                    null,
                    recorded,
                    held.RecordSceneId
                )
            );
        }

        else
        {
            _history.RecordNote(
                new(
                    held.Track,
                    replacement.Id,
                    replacementIndex,
                    replacement.Note,
                    recorded,
                    held.RecordSceneId
                )
            );
            replacement.Note = recorded;
        }

        _pendingLoopStarts[replacement.Id] = new(
            (long)Math.Floor((localStart + BeatBoundaryTolerance) / LoopBeats) + 1,
            _tracks[held.Track].Generation
        );

        if (updateSchedule)
        {
            InsertScheduledNote(replacement, held.Track);
        }

        if (changed)
        {
            _noteRevision++;
        }
    }

    private void RebuildSchedule()
    {
        for (int track = 0; track < TrackCount; track++)
        {
            foreach (Clip clip in _tracks[track].Clips)
            {
                clip.Schedule.Clear();

                foreach (StoredNote stored in clip.Notes)
                {
                    AddScheduledPair(clip.Schedule, stored, track);
                }

                clip.Schedule.Sort(ScheduledNoteComparer.Instance);
                clip.Cursor = 0;
            }
        }
    }

    private void AddScheduledPair(List<ScheduledNote> schedule, StoredNote stored, int track)
    {
        LoopNote note = stored.Note;
        schedule.Add(new(note.Beat, track, stored.Id, note.Note, note.Velocity, true));
        schedule.Add(
            new((note.Beat + note.Duration) % LoopBeats, track, stored.Id, note.Note, 0, false)
        );
    }

    private void InsertScheduledNote(StoredNote stored, int track)
    {
        Clip clip = _tracks[track].Clips[stored.SceneId];
        LoopNote note = stored.Note;
        InsertScheduled(clip, new(note.Beat, track, stored.Id, note.Note, note.Velocity, true));
        InsertScheduled(
            clip,
            new((note.Beat + note.Duration) % LoopBeats, track, stored.Id, note.Note, 0, false)
        );
    }

    private static void InsertScheduled(Clip clip, ScheduledNote note)
    {
        int index = clip.Schedule.BinarySearch(note, ScheduledNoteComparer.Instance);
        index = index < 0 ? ~index : index;
        clip.Schedule.Insert(index, note);

        if (index < clip.Cursor)
        {
            clip.Cursor++;
        }
    }

    private void RemoveScheduledNote(StoredNote stored)
    {
        LoopNote note = stored.Note;

        foreach (Track track in _tracks)
        {
            Clip clip = track.Clips[stored.SceneId];
            RemoveScheduled(clip, new(note.Beat, 0, stored.Id, 0, 0, true));
            RemoveScheduled(
                clip,
                new((note.Beat + note.Duration) % LoopBeats, 0, stored.Id, 0, 0, false)
            );
        }
    }

    private static void RemoveScheduled(Clip clip, ScheduledNote note)
    {
        int index = clip.Schedule.BinarySearch(note, ScheduledNoteComparer.Instance);

        if (index >= 0)
        {
            clip.Schedule.RemoveAt(index);

            if (index < clip.Cursor)
            {
                clip.Cursor--;
            }
        }
    }

    private void TriggerScheduled(ScheduledNote note)
    {
        if (note.IsOn)
        {
            if (_pendingLoopStarts.TryGetValue(note.Id, out PendingLoopStart pending))
            {
                if (
                    pending.Generation == _tracks[note.Track].Generation
                    && _tracks[note.Track].Cycle < pending.Cycle
                )
                {
                    return;
                }

                _ = _pendingLoopStarts.Remove(note.Id);
            }

            if (!_tracks[note.Track].IsMuted)
            {
                AddVoice(CreateVoice(note.Id, note.Track, note.Note, note.Velocity, true));
            }
        }

        else
        {
            ReleaseLoopVoices(note.Id);
        }
    }

    private void ReleaseLoopVoices(int id)
    {
        foreach (SynthVoice voice in _voices)
        {
            if (voice.Id == id && voice.IsLoop)
            {
                voice.Release(_tracks[voice.Track].Sound.ReleasePhysical);
            }
        }
    }

    private void RemoveLoopVoices(int id)
    {
        for (int voice = _voices.Count - 1; voice >= 0; voice--)
        {
            if (_voices[voice].IsLoop && _voices[voice].Id == id)
            {
                _voices.RemoveAt(voice);
            }
        }
    }

    private sealed class Track(string name, InstrumentKind instrument, string colorHex)
    {
        public string Name { get; set; } = name;
        public InstrumentKind Instrument { get; set; } = instrument;
        public string ColorHex { get; } = colorHex;
        public bool IsMuted { get; set; }
        public bool Sustain { get; set; }
        public bool IsSolo { get; set; }
        public int EditSceneId { get; set; }
        public int? ActiveSceneId { get; set; } = 0;
        public double Origin { get; set; }
        public long Cycle { get; set; } = -1;
        public long Generation { get; set; }
        public bool HasQueue { get; set; }
        public int? QueuedSceneId { get; set; }
        public double QueueBeat { get; set; }
        public Clip[] Clips { get; } =
        [
            .. Enumerable
                .Range(0, MaximumScenes)
                .Select(_ => new Clip())
        ];
        public TrackSoundState Sound { get; set; } = new(instrument);

        public float Volume
        {
            get => Sound.Level.Target;
            set => Sound.Level.SetTarget(value, true);
        }

        public List<StoredNote> Notes => Clips[EditSceneId].Notes;
    }

    private sealed class HeldNote(
        int id,
        int track,
        int note,
        int velocity,
        double? recordStart,
        bool quantizeRecording,
        int recordSceneId,
        double recordOrigin
    )
    {
        public int Id { get; } = id;
        public int Track { get; } = track;
        public int Note { get; } = note;
        public int Velocity { get; } = velocity;
        public double? RecordStart { get; set; } = recordStart;
        public bool QuantizeRecording { get; } = quantizeRecording;
        public int RecordSceneId { get; set; } = recordSceneId;
        public double RecordOrigin { get; set; } = recordOrigin;
        public bool IsKeyDown { get; set; } = true;
    }

    private sealed class StoredNote(int id, LoopNote note, int sceneId = 0)
    {
        public int Id { get; } = id;
        public int SceneId { get; } = sceneId;
        public LoopNote Note { get; set; } = note;
    }

    private readonly record struct PendingLoopStart(long Cycle, long Generation);

    private sealed class Clip
    {
        public List<StoredNote> Notes { get; } = [];
        public List<ScheduledNote> Schedule { get; } = [];
        public int Cursor { get; set; }
    }

    private readonly record struct ScheduledNote(
        double Beat,
        int Track,
        int Id,
        int Note,
        int Velocity,
        bool IsOn
    );

    private sealed class ScheduledNoteComparer : IComparer<ScheduledNote>
    {
        public static ScheduledNoteComparer Instance { get; } = new();

        public int Compare(ScheduledNote x, ScheduledNote y)
        {
            int beat = x.Beat.CompareTo(y.Beat);

            if (beat != 0)
            {
                return beat;
            }

            int kind = x.IsOn.CompareTo(y.IsOn);
            return kind != 0 ? kind : x.Id.CompareTo(y.Id);
        }
    }
}
