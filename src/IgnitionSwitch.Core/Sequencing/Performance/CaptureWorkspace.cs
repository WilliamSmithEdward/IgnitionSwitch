using IgnitionSwitch.Core.Sequencing.History;

namespace IgnitionSwitch.Core.Sequencing.Performance;

internal sealed class CaptureWorkspace(int trackCount, int maximumProjectNotes)
{
    private const int MaximumHistoryNotes = PerformanceHistory.MaximumNotes;

    public List<PerformanceNote> Source { get; } = new(MaximumHistoryNotes + (trackCount * 128));
    public Dictionary<CaptureKey, CaptureCandidate> Candidates { get; } = new(
        MaximumHistoryNotes + (trackCount * 128)
    );
    public List<NoteEdit> Changes { get; } = new(maximumProjectNotes);
    public HashSet<CaptureKey> Matched { get; } = new(maximumProjectNotes);
    public HashSet<int> ChangedIds { get; } = new(maximumProjectNotes);
    public List<CaptureCandidate> Additions { get; } = new(
        MaximumHistoryNotes + (trackCount * 128)
    );
    public int[] Counts { get; } = new int[trackCount];

    public void Clear()
    {
        Source.Clear();
        Candidates.Clear();
        Changes.Clear();
        Matched.Clear();
        ChangedIds.Clear();
        Additions.Clear();
        Array.Clear(Counts);
    }
}
