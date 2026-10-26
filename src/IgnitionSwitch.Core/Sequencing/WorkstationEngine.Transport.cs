namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private const double BeatBoundaryTolerance = 1e-9;
    private bool _loopEnabled = true;
    private bool _passCompleted;
    private double? _finishAtBeat;

    /// <summary>Gets or sets whether playback repeats. Disabling repeat finishes the current pass while preserving live notes; this preference is not saved.</summary>
    /// <remarks>A completed pass retains its end position. Play or Record restarts it from zero without resetting live notes or sustain.</remarks>
    public bool LoopEnabled
    {
        get
        {
            lock (_sync)
            {
                return _loopEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (_loopEnabled == value)
                {
                    return;
                }

                _loopEnabled = value;
                _finishAtBeat = value || _passCompleted ? null : NextLoopEnd();
            }
        }
    }

    /// <summary>Atomically ends an active recording and finalizes held notes without changing playback, position, or live ownership. Returns false when already inactive.</summary>
    public bool EndRecording()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return EndRecordingCore();
        }
    }

    private bool EndRecordingCore()
    {
        if (!_isRecording)
        {
            return false;
        }

        FinalizeHeldRecordings();
        _history.Commit();
        _isRecording = false;
        return true;
    }

    private double NextLoopEnd()
    {
        double cycle = _absoluteBeat / LoopBeats;
        double nearest = Math.Round(cycle);

        if (Math.Abs(cycle - nearest) <= BeatBoundaryTolerance / LoopBeats)
        {
            cycle = nearest;
        }

        return (Math.Floor(cycle) + 1) * LoopBeats;
    }

    private void StartPlayback()
    {
        if (!_isPlaying)
        {
            _performance.MarkDiscontinuity();
        }

        if (_passCompleted)
        {
            ResetPlaybackPosition();
        }

        else if (!_loopEnabled && _finishAtBeat is null)
        {
            _finishAtBeat = NextLoopEnd();
        }

        _isPlaying = true;
    }

    private void ResetPlaybackPosition()
    {
        _performance.MarkDiscontinuity();
        _absoluteBeat = 0;
        _scheduleCursor = 0;
        _pendingLoopStarts.Clear();
        _lastClickBeat = -1;
        _clickSamples = 0;
        _passCompleted = false;
        _finishAtBeat = _loopEnabled ? null : LoopBeats;
    }

    private void EndSequencedPlayback()
    {
        _performance.MarkDiscontinuity();
        _ = EndRecordingCore();
        _history.Commit();
        _isPlaying = false;
        _clickSamples = 0;
        _ = _voices.RemoveAll(static voice => voice.IsLoop);
        _sequencedEffects.Reset();
    }
}
