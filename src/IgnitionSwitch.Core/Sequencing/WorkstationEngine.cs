using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;

namespace IgnitionSwitch.Core.Sequencing;

/// <summary>A thread-safe, sample-clocked four-track synthesizer and MIDI loop recorder.</summary>
public sealed class WorkstationEngine : IDisposable
{
    /// <summary>The fixed rendering sample rate in frames per second.</summary>
    public const int SampleRate = 48000;

    /// <summary>The number of tracks supported by project schema version one.</summary>
    public const int TrackCount = 4;

    /// <summary>The maximum number of recorded MIDI notes stored on each track.</summary>
    public const int MaximumNotesPerTrack = 2048;
    private const double QuantizeBeats = 0.25;
    private const int MaximumVoices = 64;
    private readonly Lock _sync = new();
    private readonly List<SynthVoice> _voices = [];
    private readonly Dictionary<(int Track, int Note), HeldNote> _held = [];
    private readonly Track[] _tracks =
    [
        new("Velvet keys", InstrumentKind.Keys, "#9B8CFF"),
        new("Round bass", InstrumentKind.Bass, "#5CD7C4"),
        new("Soft atmosphere", InstrumentKind.Atmosphere, "#E7AF68"),
        new("Pocket drums", InstrumentKind.Drums, "#F279A2")
    ];
    private ScheduledNote[] _schedule = [];
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
                _masterVolume = value;
            }
        }
    }

    /// <summary>Gets the audio-clock position within the sixteen-beat loop.</summary>
    public double BeatPosition
    {
        get
        {
            lock (_sync)
            {
                return _absoluteBeat % LoopBeats;
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
            _isPlaying = true;
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
            FinalizeHeldRecordings();
            _isPlaying = false;
            _isRecording = false;
            _clickSamples = 0;
            _ = _voices.RemoveAll(voice => voice.IsLoop);
        }
    }

    /// <summary>Ends recording, silences voices, resets sustain, and rewinds the transport.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            FinalizeHeldRecordings();
            _isPlaying = false;
            _isRecording = false;
            _absoluteBeat = 0;
            _scheduleCursor = 0;
            _lastClickBeat = -1;
            _clickSamples = 0;
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

    /// <summary>Toggles quantized overdubbing, starting playback when necessary.</summary>
    public void ToggleRecording()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_isRecording)
            {
                FinalizeHeldRecordings();
            }

            else
            {
                _isPlaying = true;
            }

            _isRecording = !_isRecording;
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
            HeldNote held = new(id, track, note, velocity, _isRecording ? _absoluteBeat : null);

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
            _tracks[_selectedTrack].Notes.Clear();

            foreach (HeldNote held in _held.Values.Where(held => held.Track == _selectedTrack))
            {
                held.RecordStart = null;
            }

            _ = _voices.RemoveAll(voice => voice.Track == _selectedTrack && voice.IsLoop);
            RebuildSchedule(!_isPlaying);
        }
    }

    /// <summary>Mutes or unmutes a specific track.</summary>
    public void SetTrackMuted(int track, bool muted)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
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

        lock (_sync)
        {
            ThrowIfDisposed();
            double beatsPerSample = _tempo / (60 * SampleRate);

            for (int sample = 0; sample < samples.Length; sample += 2)
            {
                double position = _absoluteBeat % LoopBeats;

                if (_isPlaying)
                {
                    while (
                        _scheduleCursor < _schedule.Length
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
                        (float)(Math.Sin(2 * Math.PI * _clickFrequency * _clickSamples / SampleRate)
                            * _clickSamples
                            / (SampleRate / 40.0)
                            * 0.08);
                    _clickSamples--;
                }

                mixed *= _masterVolume;
                float output = MathF.Tanh(mixed);
                samples[sample] = output;
                samples[sample + 1] = output;

                if (_isPlaying)
                {
                    _absoluteBeat += beatsPerSample;

                    if (_absoluteBeat % LoopBeats < position)
                    {
                        _scheduleCursor = 0;
                    }
                }
            }
        }
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

            RebuildSchedule(true);
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
            _schedule = [];
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
        foreach (SynthVoice voice in _voices.Where(voice => voice.Id == held.Id))
        {
            voice.Release();
        }

        if (held.RecordStart is double start)
        {
            RecordNote(held, _absoluteBeat - start);
            held.RecordStart = null;
        }
    }

    private void FinalizeHeldRecordings()
    {
        foreach (HeldNote held in _held.Values)
        {
            if (held.RecordStart is double start)
            {
                RecordNote(held, _absoluteBeat - start);
                held.RecordStart = null;
            }
        }
    }

    private void RecordNote(HeldNote held, double duration)
    {
        if (held.RecordStart is not double start)
        {
            return;
        }

        double beat = Math.Round(start / QuantizeBeats) * QuantizeBeats % LoopBeats;
        duration = Math.Clamp(
            Math.Round(duration / QuantizeBeats) * QuantizeBeats,
            QuantizeBeats,
            LoopBeats
        );
        Track track = _tracks[held.Track];
        _ = track.Notes.RemoveAll(stored =>
        {
            if (stored.Note.Note != held.Note || stored.Note.Beat != beat)
            {
                return false;
            }

            ReleaseLoopVoices(stored.Id);
            return true;
        });

        if (track.Notes.Count < MaximumNotesPerTrack)
        {
            track.Notes.Add(
                new StoredNote(
                    NextVoiceId(),
                    new LoopNote(held.Note, held.Velocity, beat, duration)
                )
            );
            RebuildSchedule(false);
        }
    }

    private void RebuildSchedule(bool includeCurrent)
    {
        List<ScheduledNote> schedule = [];

        for (int track = 0; track < TrackCount; track++)
        {
            foreach (StoredNote stored in _tracks[track].Notes)
            {
                LoopNote note = stored.Note;
                schedule.Add(
                    new ScheduledNote(note.Beat, track, stored.Id, note.Note, note.Velocity, true)
                );
                schedule.Add(
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

        _schedule =
        [
            .. schedule
                .OrderBy(note => note.Beat)
                .ThenBy(note => note.IsOn)
        ];
        double position = _absoluteBeat % LoopBeats;
        _scheduleCursor = 0;

        if (_isPlaying && !includeCurrent)
        {
            while (
                _scheduleCursor < _schedule.Length
                && _schedule[_scheduleCursor].Beat <= position + 1e-9
            )
            {
                _scheduleCursor++;
            }
        }
    }

    private void TriggerScheduled(ScheduledNote note)
    {
        if (note.IsOn)
        {
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

    private sealed class Track(string name, InstrumentKind instrument, string colorHex)
    {
        public string Name { get; set; } = name;
        public InstrumentKind Instrument { get; set; } = instrument;
        public string ColorHex { get; } = colorHex;
        public bool IsMuted { get; set; }
        public bool Sustain { get; set; }
        public float Volume { get; set; } = 0.8f;
        public List<StoredNote> Notes { get; } = [];
    }

    private sealed class HeldNote(int id, int track, int note, int velocity, double? recordStart)
    {
        public int Id { get; } = id;
        public int Track { get; } = track;
        public int Note { get; } = note;
        public int Velocity { get; } = velocity;
        public double? RecordStart { get; set; } = recordStart;
        public bool IsKeyDown { get; set; } = true;
    }

    private sealed record StoredNote(int Id, LoopNote Note);

    private sealed record ScheduledNote(
        double Beat,
        int Track,
        int Id,
        int Note,
        int Velocity,
        bool IsOn
    );
}
