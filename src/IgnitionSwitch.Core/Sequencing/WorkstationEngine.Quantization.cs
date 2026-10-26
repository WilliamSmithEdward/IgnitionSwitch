using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly HashSet<(int Note, double Beat)> _quantizedSlots = new(MaximumNotesPerTrack);
    private bool _recordingQuantizationEnabled = true;

    /// <summary>Gets or sets whether newly played recorded notes snap to a sixteenth-note grid. This preference is not saved in projects.</summary>
    /// <remarks>Each note retains the policy active at note-on; changing the preference does not alter held or recorded notes.</remarks>
    public bool RecordingQuantizationEnabled
    {
        get
        {
            lock (_sync)
            {
                return _recordingQuantizationEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _recordingQuantizationEnabled = value;
            }
        }
    }

    /// <summary>Ends recording and snaps the selected track clip's note onsets in the editing scene to sixteenths, preserving durations. Returns whether the quantization changed content.</summary>
    /// <remarks>
    /// Midpoint onsets move forward and the loop end wraps to zero. The last stored note wins a
    /// same-pitch collision. Changes form one undo step; live voices, transport, and pending-loop
    /// eligibility are preserved. An already aligned track adds no history step or note revision.
    /// </remarks>
    public bool QuantizeSelectedTrack()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            FinalizeHeldRecordings();
            _isRecording = false;
            _history.Commit();
            _quantizedSlots.Clear();
            Track track = _tracks[_selectedTrack];
            bool changed = false;

            for (int index = track.Notes.Count - 1; index >= 0; index--)
            {
                StoredNote stored = track.Notes[index];
                LoopNote original = stored.Note;
                double beat = QuantizeOnset(original.Beat);
                bool survives = _quantizedSlots.Add((original.Note, beat));

                if (survives && original.Beat == beat)
                {
                    continue;
                }

                RemoveLoopVoices(stored.Id);

                if (survives)
                {
                    LoopNote quantized = original with { Beat = beat };
                    _history.RecordNote(
                        new(_selectedTrack, stored.Id, index, original, quantized, _editingSceneId)
                    );
                    stored.Note = quantized;
                }

                else
                {
                    _history.RecordNote(
                        new(_selectedTrack, stored.Id, index, original, null, _editingSceneId)
                    );
                    _ = _pendingLoopStarts.Remove(stored.Id);
                    track.Notes.RemoveAt(index);
                }

                changed = true;
            }

            if (changed)
            {
                RebuildSchedule();
                AlignHistorySchedule();
                _noteRevision++;
                _history.Commit();
            }

            return changed;
        }
    }

    private double WrapClipBeat(double beat)
    {
        beat %= LoopBeats;

        if (beat < 0)
        {
            beat += LoopBeats;
        }

        return beat < BeatBoundaryTolerance || LoopBeats - beat < BeatBoundaryTolerance ? 0 : beat;
    }

    private double QuantizeOnset(double beat)
    {
        return RoundToQuantizationGrid(beat % LoopBeats) % LoopBeats;
    }

    private static double RoundToQuantizationGrid(double beats)
    {
        return Math.Round(beats / QuantizeBeats, MidpointRounding.AwayFromZero) * QuantizeBeats;
    }
}
