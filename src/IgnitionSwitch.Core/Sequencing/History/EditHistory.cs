using System.Diagnostics;

namespace IgnitionSwitch.Core.Sequencing.History;

internal sealed class EditHistory
{
    private const int MaximumSteps = 64;
    private const int MaximumNoteDeltas = 32768;
    private static readonly TimeSpan GestureIdle = TimeSpan.FromMilliseconds(500);
    private readonly List<SessionEdit> undo = new(MaximumSteps);
    private readonly List<SessionEdit> redo = new(MaximumSteps);
    private SessionEdit? pending;
    private long lastScalarTimestamp;
    private int retainedNoteCount;

    public bool CanUndo => pending?.HasChanges == true || undo.Count > 0;
    public bool CanRedo => pending?.HasChanges != true && redo.Count > 0;

    public void RecordNote(NoteEdit edit)
    {
        if (!edit.HasChanges)
        {
            return;
        }

        if (pending?.IsNoteEdit != true)
        {
            Commit();
            pending = new SessionEdit(true, default);
        }

        pending.AddNote(edit);
        Trim(pending.RetainedNoteCount);
    }

    public void RecordScalar(ScalarEdit edit, bool continuous)
    {
        if (edit.Before == edit.After)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();

        if (
            continuous
            && pending is { IsNoteEdit: false } current
            && current.Scalar.Property == edit.Property
            && current.Scalar.Track == edit.Track
            && Stopwatch.GetElapsedTime(lastScalarTimestamp, timestamp) <= GestureIdle
        )
        {
            current.SetScalarAfter(edit.After);
        }

        else
        {
            Commit();
            pending = new SessionEdit(false, edit);
        }

        lastScalarTimestamp = timestamp;

        if (!continuous)
        {
            Commit();
        }
    }

    public void Commit()
    {
        if (pending is not { } edit)
        {
            return;
        }

        pending = null;

        if (!edit.HasChanges)
        {
            return;
        }

        foreach (SessionEdit discarded in redo)
        {
            retainedNoteCount -= discarded.RetainedNoteCount;
        }

        redo.Clear();
        undo.Add(edit);
        retainedNoteCount += edit.RetainedNoteCount;
        Trim(0);
    }

    public SessionEdit? TakeUndo()
    {
        Commit();

        if (undo.Count == 0)
        {
            return null;
        }

        SessionEdit edit = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(edit);
        return edit;
    }

    public SessionEdit? TakeRedo()
    {
        Commit();

        if (redo.Count == 0)
        {
            return null;
        }

        SessionEdit edit = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(edit);
        return edit;
    }

    public void Reset()
    {
        pending = null;
        undo.Clear();
        redo.Clear();
        retainedNoteCount = 0;
    }

    private void Trim(int pendingNotes)
    {
        while (
            undo.Count + redo.Count > MaximumSteps
            || retainedNoteCount + pendingNotes > MaximumNoteDeltas
        )
        {
            List<SessionEdit> oldest = undo.Count > 0 ? undo : redo;
            retainedNoteCount -= oldest[0].RetainedNoteCount;
            oldest.RemoveAt(0);
        }
    }
}
