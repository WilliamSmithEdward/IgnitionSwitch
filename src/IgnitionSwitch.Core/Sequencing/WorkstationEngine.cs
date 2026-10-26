using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing.History;

namespace IgnitionSwitch.Core.Sequencing;

/// <summary>A thread-safe, sample-clocked four-track synthesizer and MIDI loop recorder.</summary>
public sealed partial class WorkstationEngine : IDisposable
{
    /// <summary>The fixed rendering sample rate in frames per second.</summary>
    public const int SampleRate = 48000;

    /// <summary>The number of tracks supported by project schema version one.</summary>
    public const int TrackCount = 4;

    /// <summary>The maximum number of recorded MIDI notes stored on each track.</summary>
    public const int MaximumNotesPerTrack = 2048;
    private const double QuantizeBeats = 0.25;
    private const int MaximumVoices = 64;
    private const int RenderQuantumFrames = 64;
    private const int MaximumProjectNotes = TrackCount * MaximumNotesPerTrack;
    private readonly Lock _sync = new();
    private readonly List<SynthVoice> _voices = new(MaximumVoices);
    private readonly Dictionary<(int Track, int Note), HeldNote> _held = new(TrackCount * 128);
    private readonly Dictionary<int, long> _pendingLoopStarts = new(MaximumProjectNotes);
    private readonly Track[] _tracks =
    [
        new("Velvet keys", InstrumentKind.Keys, "#9B8CFF"),
        new("Round bass", InstrumentKind.Bass, "#5CD7C4"),
        new("Soft atmosphere", InstrumentKind.Atmosphere, "#E7AF68"),
        new("Pocket drums", InstrumentKind.Drums, "#F279A2")
    ];
    private readonly List<ScheduledNote> _schedule = new(MaximumProjectNotes * 2);
    private int _scheduleCursor;
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
                _history.RecordScalar(new(HistoryProperty.Tempo, -1, _tempo, value), true);
                _tempo = value;
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
                _history.RecordScalar(
                    new(HistoryProperty.MasterVolume, -1, _masterVolume, value),
                    true
                );
                _masterVolume = value;
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

    /// <summary>Gets a revision that changes only when recorded note content is added, replaced, cleared, or loaded.</summary>
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

    /// <summary>Gets detached, read-only track state snapshots in display order.</summary>
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
                                track.Notes.Count,
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

            foreach (Track track in _tracks)
            {
                track.Sustain = false;
            }
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
            AddVoice(new SynthVoice(id, track, note, velocity, _tracks[track].Instrument, false));
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
                _isRecording ? _absoluteBeat : null,
                _recordingQuantizationEnabled
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
            _pitchRatio = 1;

            foreach (Track track in _tracks)
            {
                track.Sustain = false;
            }
        }
    }

    /// <summary>Clears recorded notes on the selected track without changing its instrument or mixer state.</summary>
    public void ClearSelectedTrack()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            Track selected = _tracks[_selectedTrack];

            if (selected.Notes.Count > 0)
            {
                _history.Commit();

                for (int index = selected.Notes.Count - 1; index >= 0; index--)
                {
                    StoredNote stored = selected.Notes[index];
                    _history.RecordNote(new(_selectedTrack, stored.Id, index, stored.Note, null));
                }

                foreach (StoredNote stored in selected.Notes)
                {
                    _ = _pendingLoopStarts.Remove(stored.Id);
                }

                RemoveScheduledTrack(_selectedTrack);
                selected.Notes.Clear();
                _noteRevision++;
                _history.Commit();
            }

            foreach (HeldNote held in _held.Values.Where(held => held.Track == _selectedTrack))
            {
                held.RecordStart = null;
            }

            _ = _voices.RemoveAll(voice => voice.Track == _selectedTrack && voice.IsLoop);
        }
    }

    /// <summary>Mutes or unmutes a specific track.</summary>
    public void SetTrackMuted(int track, bool muted)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
            _history.RecordScalar(
                new(
                    HistoryProperty.TrackMute,
                    track,
                    _tracks[track].IsMuted ? 1 : 0,
                    muted ? 1 : 0
                ),
                false
            );
            _tracks[track].IsMuted = muted;
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
            _history.RecordScalar(
                new(HistoryProperty.TrackVolume, track, _tracks[track].Volume, volume),
                true
            );
            _tracks[track].Volume = volume;
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
                    double position = _absoluteBeat % LoopBeats;

                    if (_isPlaying)
                    {
                        while (
                            _scheduleCursor < _schedule.Count
                            && _schedule[_scheduleCursor].Beat <= position + 1e-9
                        )
                        {
                            TriggerScheduled(_schedule[_scheduleCursor++]);
                        }

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

                    float mixed = 0;

                    for (int voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
                    {
                        SynthVoice voice = _voices[voiceIndex];
                        float value = voice.Render(_pitchRatio);
                        Track track = _tracks[voice.Track];

                        if (!track.IsMuted)
                        {
                            mixed += value * track.Volume;
                        }

                        if (voice.IsFinished)
                        {
                            _voices.RemoveAt(voiceIndex);
                        }
                    }

                    if (_clickSamples > 0)
                    {
                        mixed +=
                            (float)(Math.Sin(
                                2 * Math.PI * _clickFrequency * _clickSamples / SampleRate
                            )
                                * _clickSamples
                                / (SampleRate / 40.0)
                                * 0.08);
                        _clickSamples--;
                    }

                    mixed *= _masterVolume;
                    float output = MathF.Tanh(mixed);
                    samples[sample] = output;
                    samples[sample + 1] = output;
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
                            _scheduleCursor = _schedule.Count;
                        }

                        else if (_absoluteBeat % LoopBeats < position)
                        {
                            _scheduleCursor = 0;
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
            TrackSnapshot[] tracks =
            [
                .. _tracks.Select(
                    track => new TrackSnapshot(
                        track.Name,
                        track.Instrument,
                        track.IsMuted,
                        track.Volume,
                        Array.AsReadOnly(
                            track.Notes
                                .Select(note => note.Note)
                                .ToArray()
                        )
                    )
                )
            ];
            return new SessionSnapshot(
                1,
                _tempo,
                LoopBars,
                _masterVolume,
                Array.AsReadOnly(tracks)
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
            _tempo = session.Tempo;
            _masterVolume = session.MasterVolume;

            for (int track = 0; track < TrackCount; track++)
            {
                TrackSnapshot source = session.Tracks[track];
                Track target = _tracks[track];
                target.Name = source.Name;
                target.Instrument = source.Instrument;
                target.IsMuted = source.IsMuted;
                target.Volume = source.Volume;
                target.Notes.Clear();

                foreach (LoopNote note in source.Notes)
                {
                    target.Notes.Add(new StoredNote(NextVoiceId(), note));
                }
            }

            RebuildSchedule();
            _noteRevision++;
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
            _schedule.Clear();
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
            _voices.RemoveAt(0);
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
                voice.Release();
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

        double beat = held.QuantizeRecording ? QuantizeOnset(start) : start % LoopBeats;
        duration = held.QuantizeRecording
            ? Math.Clamp(RoundToQuantizationGrid(duration), QuantizeBeats, LoopBeats)
            : Math.Clamp(duration, _tempo / (60 * SampleRate), LoopBeats);
        Track track = _tracks[held.Track];
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
                _history.RecordNote(new(held.Track, stored.Id, index, stored.Note, null));
                track.Notes.RemoveAt(index);
                removedDuplicate = true;
            }
        }

        if (replacement is null && track.Notes.Count >= MaximumNotesPerTrack)
        {
            return;
        }

        LoopNote recorded = new(held.Note, held.Velocity, beat, duration);
        bool changed = replacement is null || replacement.Note != recorded || removedDuplicate;

        if (replacement is null)
        {
            replacement = new StoredNote(NextVoiceId(), recorded);
            track.Notes.Add(replacement);
            _history.RecordNote(
                new(held.Track, replacement.Id, track.Notes.Count - 1, null, recorded)
            );
        }

        else
        {
            _history.RecordNote(
                new(held.Track, replacement.Id, replacementIndex, replacement.Note, recorded)
            );
            replacement.Note = recorded;
        }

        _pendingLoopStarts[replacement.Id] = (long)Math.Floor(start / LoopBeats) + 1;

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
        _schedule.Clear();

        for (int track = 0; track < TrackCount; track++)
        {
            foreach (StoredNote stored in _tracks[track].Notes)
            {
                LoopNote note = stored.Note;
                _schedule.Add(
                    new ScheduledNote(note.Beat, track, stored.Id, note.Note, note.Velocity, true)
                );
                _schedule.Add(
                    new ScheduledNote(
                        (note.Beat + note.Duration) % LoopBeats,
                        track,
                        stored.Id,
                        note.Note,
                        0,
                        false
                    )
                );
            }
        }

        _schedule.Sort(ScheduledNoteComparer.Instance);
        _scheduleCursor = 0;
    }

    private void InsertScheduledNote(StoredNote stored, int track)
    {
        LoopNote note = stored.Note;
        InsertScheduled(
            new ScheduledNote(note.Beat, track, stored.Id, note.Note, note.Velocity, true)
        );
        InsertScheduled(
            new ScheduledNote(
                (note.Beat + note.Duration) % LoopBeats,
                track,
                stored.Id,
                note.Note,
                0,
                false
            )
        );
    }

    private void InsertScheduled(ScheduledNote note)
    {
        int index = _schedule.BinarySearch(note, ScheduledNoteComparer.Instance);
        index = index < 0 ? ~index : index;
        _schedule.Insert(index, note);

        if (index < _scheduleCursor)
        {
            _scheduleCursor++;
        }
    }

    private void RemoveScheduledNote(StoredNote stored)
    {
        LoopNote note = stored.Note;
        RemoveScheduled(new ScheduledNote(note.Beat, 0, stored.Id, 0, 0, true));
        RemoveScheduled(
            new ScheduledNote((note.Beat + note.Duration) % LoopBeats, 0, stored.Id, 0, 0, false)
        );
    }

    private void RemoveScheduled(ScheduledNote note)
    {
        int index = _schedule.BinarySearch(note, ScheduledNoteComparer.Instance);

        if (index >= 0)
        {
            _schedule.RemoveAt(index);

            if (index < _scheduleCursor)
            {
                _scheduleCursor--;
            }
        }
    }

    private void RemoveScheduledTrack(int track)
    {
        int read = 0;
        int write = 0;
        int removedBeforeCursor = 0;

        while (read < _schedule.Count)
        {
            ScheduledNote note = _schedule[read];

            if (note.Track != track)
            {
                _schedule[write++] = note;
            }

            else if (read < _scheduleCursor)
            {
                removedBeforeCursor++;
            }

            read++;
        }

        _schedule.RemoveRange(write, _schedule.Count - write);
        _scheduleCursor -= removedBeforeCursor;
    }

    private void TriggerScheduled(ScheduledNote note)
    {
        if (note.IsOn)
        {
            if (_pendingLoopStarts.TryGetValue(note.Id, out long firstCycle))
            {
                if ((long)Math.Floor(_absoluteBeat / LoopBeats) < firstCycle)
                {
                    return;
                }

                _ = _pendingLoopStarts.Remove(note.Id);
            }

            if (!_tracks[note.Track].IsMuted)
            {
                AddVoice(
                    new SynthVoice(
                        note.Id,
                        note.Track,
                        note.Note,
                        note.Velocity,
                        _tracks[note.Track].Instrument,
                        true
                    )
                );
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
                voice.Release();
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
        public float Volume { get; set; } = 0.8f;
        public List<StoredNote> Notes { get; } = new(MaximumNotesPerTrack);
    }

    private sealed class HeldNote(
        int id,
        int track,
        int note,
        int velocity,
        double? recordStart,
        bool quantizeRecording
    )
    {
        public int Id { get; } = id;
        public int Track { get; } = track;
        public int Note { get; } = note;
        public int Velocity { get; } = velocity;
        public double? RecordStart { get; set; } = recordStart;
        public bool QuantizeRecording { get; } = quantizeRecording;
        public bool IsKeyDown { get; set; } = true;
    }

    private sealed class StoredNote(int id, LoopNote note)
    {
        public int Id { get; } = id;
        public LoopNote Note { get; set; } = note;
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
