using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing.History;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly EditHistory _history = new();

    /// <summary>Gets whether completed note or mixer edits can be undone, including the open edit group.</summary>
    public bool CanUndo
    {
        get
        {
            lock (_sync)
            {
                return _history.CanUndo;
            }
        }
    }

    /// <summary>Gets whether an undone edit can be restored without discarding a newer edit.</summary>
    public bool CanRedo
    {
        get
        {
            lock (_sync)
            {
                return _history.CanRedo;
            }
        }
    }

    /// <summary>Closes a recording part or continuous mixer gesture without adding an empty history step.</summary>
    /// <remarks>
    /// Recording parts end at recording or transport boundaries and actual mixer or clear edits.
    /// Consecutive writes to one continuous mixer target coalesce until this call, a different edit,
    /// or 500 milliseconds of idle time. Loading a project clears all transient edit history.
    /// </remarks>
    public void CommitEdit()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _history.Commit();
        }
    }

    /// <summary>Ends recording and reverses the latest content edit, preserving transport position and live notes.</summary>
    /// <remarks>History retains at most 64 edits and 32768 note deltas. Changed sequenced notes wait for their next onset.</remarks>
    public bool Undo()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            FinalizeHeldRecordings();
            _isRecording = false;
            SessionEdit? edit = _history.TakeUndo();

            if (edit is null)
            {
                return false;
            }

            ApplyHistoryEdit(edit, true);
            return true;
        }
    }

    /// <summary>Ends recording and restores the latest undone content edit, preserving transport position and live notes.</summary>
    public bool Redo()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            FinalizeHeldRecordings();
            _isRecording = false;
            SessionEdit? edit = _history.TakeRedo();

            if (edit is null)
            {
                return false;
            }

            ApplyHistoryEdit(edit, false);
            return true;
        }
    }

    private void ApplyHistoryEdit(SessionEdit edit, bool reverse)
    {
        if (edit.Scene is SceneEdit scene)
        {
            ApplySceneEdit(scene, reverse);
            return;
        }

        if (!edit.IsNoteEdit)
        {
            ScalarEdit scalar = edit.Scalar;
            double value = reverse ? scalar.Before : scalar.After;

            ApplyParameterValue(scalar.Address, value);

            return;
        }

        for (int step = 0; step < edit.Notes.Count; step++)
        {
            int index = reverse ? edit.Notes.Count - 1 - step : step;
            NoteEdit note = edit.Notes[index];

            if (note.HasChanges)
            {
                ApplyHistoryNote(note, reverse);
            }
        }

        RebuildSchedule();
        AlignHistorySchedule();
        _noteRevision++;
    }

    private void AlignHistorySchedule()
    {
        foreach (Track track in _tracks)
        {
            if (track.ActiveSceneId is not int sceneId)
            {
                continue;
            }

            Clip clip = track.Clips[sceneId];
            double position = WrapClipBeat(_absoluteBeat - track.Origin);
            clip.Cursor = 0;

            while (
                clip.Cursor < clip.Schedule.Count
                && clip.Schedule[clip.Cursor].Beat < position - BeatBoundaryTolerance
            )
            {
                clip.Cursor++;
            }

            track.Cycle = (long)Math.Floor(
                (_absoluteBeat - track.Origin + BeatBoundaryTolerance) / LoopBeats
            );
        }
    }

    private void ApplyHistoryNote(NoteEdit edit, bool reverse, bool removeLoopVoices = true)
    {
        Clip track = _tracks[edit.Track].Clips[edit.SceneId];
        LoopNote? target = reverse ? edit.Before : edit.After;
        int index = edit.Index < track.Notes.Count && track.Notes[edit.Index].Id == edit.Id
            ? edit.Index
            : -1;

        for (int candidate = 0; index < 0 && candidate < track.Notes.Count; candidate++)
        {
            if (track.Notes[candidate].Id == edit.Id)
            {
                index = candidate;
                break;
            }
        }

        if (removeLoopVoices)
        {
            RemoveLoopVoices(edit.Id);
        }

        _ = _pendingLoopStarts.Remove(edit.Id);

        if (index >= 0)
        {
            StoredNote stored = track.Notes[index];

            if (target is null)
            {
                track.Notes.RemoveAt(index);
            }

            else
            {
                stored.Note = target;
            }
        }

        else if (target is not null)
        {
            var stored = new StoredNote(edit.Id, target, edit.SceneId);
            track.Notes.Insert(Math.Min(edit.Index, track.Notes.Count), stored);
        }
    }
}
