using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private static long nextEngineIdentity;
    private readonly long _engineIdentity = Interlocked.Increment(ref nextEngineIdentity);
    private long _nextLiveGateId;
    private double _modulationPhase;
    private bool _modulationRequested;

    /// <summary>Begins a live gate and returns an engine-scoped handle, with optional normalized initial modulation and pressure. Velocity zero releases the note and returns default.</summary>
    /// <remarks>Retriggering the same track/pitch invalidates its previous handle. Initial expression is atomic with note-on; all amounts use a 5 ms sample ramp. Expression is transient and is not recorded or saved.</remarks>
    public LiveNoteHandle BeginLiveNote(
        int track,
        int note,
        int velocity,
        bool retainForCapture = true,
        double modulation = 0,
        double pressure = 0
    )
    {
        ValidateTrack(track);
        ArgumentOutOfRangeException.ThrowIfNegative(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 127);
        ArgumentOutOfRangeException.ThrowIfNegative(velocity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(velocity, 127);
        ValidateRange(modulation, 0, 1, nameof(modulation));
        ValidateRange(pressure, 0, 1, nameof(pressure));

        if (velocity == 0)
        {
            NoteOff(track, note);
            return default;
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            return BeginLiveNoteCore(
                track,
                note,
                velocity,
                retainForCapture,
                (float)modulation,
                (float)pressure
            );
        }
    }

    /// <summary>Sets normalized wheel modulation and combined pressure for one current live gate, returning false for default, foreign, superseded, released, finished, or stolen handles.</summary>
    /// <remarks>Current sustained gates remain eligible until their real release. Modulation gives melodic live voices 5.5 Hz vibrato up to 30 cents; pressure brightens live instruments and adds at most 15 percent gain. Sequenced notes and saved parameters are unaffected.</remarks>
    public bool SetLiveExpression(LiveNoteHandle handle, double modulation, double pressure)
    {
        ValidateRange(modulation, 0, 1, nameof(modulation));
        ValidateRange(pressure, 0, 1, nameof(pressure));

        lock (_sync)
        {
            ThrowIfDisposed();

            if (handle.EngineId != _engineIdentity || !handle.IsValid)
            {
                return false;
            }

            foreach (SynthVoice voice in _voices)
            {
                if (voice.MatchesLiveGate(handle.GateId))
                {
                    voice.SetLiveExpression((float)modulation, (float)pressure);
                    _modulationRequested |= voice.NeedsModulation;
                    return true;
                }
            }

            return false;
        }
    }

    private LiveNoteHandle BeginLiveNoteCore(
        int track,
        int note,
        int velocity,
        bool retainForCapture,
        float modulation,
        float pressure
    )
    {
        if (_held.Remove((track, note), out HeldNote? previous))
        {
            ReleaseHeld(previous);
        }

        EndLiveGate(track, note);
        int id = NextVoiceId();
        _nextLiveGateId = unchecked(_nextLiveGateId + 1);

        if (_nextLiveGateId == 0)
        {
            _nextLiveGateId++;
        }

        SynthVoice voice = CreateVoice(id, track, note, velocity, false, _nextLiveGateId);
        voice.SetLiveExpression(modulation, pressure);
        _modulationRequested |= voice.NeedsModulation;
        AddVoice(voice);
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

        return new(_engineIdentity, _nextLiveGateId);
    }

    private void EndLiveGate(int track, int note)
    {
        foreach (SynthVoice voice in _voices)
        {
            if (voice.Track == track && voice.Note == note && !voice.IsLoop)
            {
                voice.EndLiveGate();
            }
        }
    }

    private double AdvanceModulation()
    {
        _modulationPhase += 5.5 / SampleRate;

        if (_modulationPhase >= 1)
        {
            _modulationPhase -= 1;
        }

        return _modulationRequested
            ? Math.Pow(2, Math.Sin(2 * Math.PI * _modulationPhase) * (30.0 / 1200))
            : 1;
    }

    private void ResetLiveExpression()
    {
        _modulationRequested = false;
        _modulationPhase = 0;
    }
}
