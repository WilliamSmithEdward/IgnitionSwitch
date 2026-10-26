namespace IgnitionSwitch.Core.Sequencing.History;

internal sealed class SessionEdit(bool isNoteEdit, ScalarEdit scalar)
{
    private readonly List<NoteEdit> notes = [];
    private readonly Dictionary<int, int> noteIndices = [];
    private int changedNotes;

    public bool IsNoteEdit { get; } = isNoteEdit;
    public ScalarEdit Scalar { get; private set; } = scalar;
    public IReadOnlyList<NoteEdit> Notes => notes;
    public int RetainedNoteCount => notes.Count;
    public bool HasChanges => IsNoteEdit ? changedNotes > 0 : Scalar.Before != Scalar.After;

    public void AddNote(NoteEdit edit)
    {
        if (noteIndices.TryGetValue(edit.Id, out int index))
        {
            NoteEdit previous = notes[index];

            if (previous.HasChanges)
            {
                changedNotes--;
            }

            edit = previous with { After = edit.After };
            notes[index] = edit;
        }

        else
        {
            noteIndices.Add(edit.Id, notes.Count);
            notes.Add(edit);
        }

        if (edit.HasChanges)
        {
            changedNotes++;
        }
    }

    public void SetScalarAfter(double value)
    {
        Scalar = Scalar with { After = value };
    }
}
