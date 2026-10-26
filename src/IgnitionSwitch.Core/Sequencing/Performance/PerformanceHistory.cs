namespace IgnitionSwitch.Core.Sequencing.Performance;

internal sealed class PerformanceHistory(int trackCount)
{
    internal const int MaximumNotes = 8192;
    private const int NotesPerTrack = 128;
    private const double PhraseBeats = 16;
    private const double BarBeats = 4;
    private const double BoundaryTolerance = 1e-9;
    private readonly PerformanceNote[] completed = new PerformanceNote[MaximumNotes];
    private readonly PerformanceGate[] active = new PerformanceGate[trackCount * NotesPerTrack];
    private int first;
    private int count;
    private int activeCount;
    private long nextOrdinal;
    private long epoch;
    private double beat;
    private double origin;
    private double projectOffset;
    private double latestEnd;
    private double lastActivity;
    private double lostThrough = double.NegativeInfinity;
    private bool projectAligned;
    private bool discontinuity = true;

    public bool HasRecentPerformance => count > 0 || activeCount > 0;
    public long FrameCount { get; private set; }

    public void Advance(double beatsPerFrame)
    {
        FrameCount++;
        beat += beatsPerFrame;
    }

    public void MarkDiscontinuity()
    {
        discontinuity = true;
    }

    public void NoteOn(
        int track,
        int note,
        int velocity,
        bool retain,
        bool quantize,
        bool playing,
        double absoluteBeat,
        double minimumDuration
    )
    {
        CloseNote(track, note);

        if (!retain)
        {
            return;
        }

        if (discontinuity || epoch == 0 || (activeCount == 0 && beat - lastActivity >= PhraseBeats))
        {
            BeginEpoch(playing, absoluteBeat);
        }

        active[(track * NotesPerTrack) + note] = new(
            ++nextOrdinal,
            epoch,
            velocity,
            beat,
            quantize,
            minimumDuration
        );
        activeCount++;
        lastActivity = beat;
    }

    public void CloseNote(int track, int note)
    {
        int index = (track * NotesPerTrack) + note;
        PerformanceGate gate = active[index];

        if (gate.Ordinal == 0)
        {
            return;
        }

        active[index] = default;
        activeCount--;
        double end = Math.Max(beat, Math.Max(gate.Start, origin) + gate.MinimumDuration);
        Add(
            new(
                gate.Ordinal,
                gate.Epoch,
                track,
                note,
                gate.Velocity,
                gate.Start,
                end,
                gate.Quantize,
                gate.MinimumDuration
            )
        );
        latestEnd = Math.Max(latestEnd, end);
        lastActivity = beat;
    }

    public void CloseAll()
    {
        for (int index = 0; index < active.Length; index++)
        {
            if (active[index].Ordinal != 0)
            {
                CloseNote(index / NotesPerTrack, index % NotesPerTrack);
            }
        }
    }

    public PerformanceWindow CopyWindow(List<PerformanceNote> destination)
    {
        double end;

        if (activeCount > 0)
        {
            end = beat;

            foreach (PerformanceGate gate in active)
            {
                if (gate.Ordinal != 0)
                {
                    end = Math.Max(end, Math.Max(gate.Start, origin) + gate.MinimumDuration);
                }
            }
        }

        else
        {
            double gridOrigin = projectAligned ? projectOffset : origin;
            double bar = (latestEnd - gridOrigin) / BarBeats;
            double nearest = Math.Round(bar);

            if (Math.Abs(bar - nearest) <= BoundaryTolerance / BarBeats)
            {
                bar = nearest;
            }

            end = gridOrigin + (Math.Ceiling(bar) * BarBeats);
        }

        double start = Math.Max(origin, end - PhraseBeats);
        bool truncated = lostThrough > start + BoundaryTolerance;

        for (int offset = 0; offset < count; offset++)
        {
            PerformanceNote note = completed[(first + offset) % MaximumNotes];

            if (
                note.Epoch == epoch
                && note.Start < end - BoundaryTolerance
                && note.End > start + BoundaryTolerance
            )
            {
                destination.Add(note);
            }
        }

        for (int index = 0; index < active.Length; index++)
        {
            PerformanceGate gate = active[index];

            if (gate.Ordinal == 0)
            {
                continue;
            }

            double gateEnd = Math.Max(beat, Math.Max(gate.Start, start) + gate.MinimumDuration);

            if (gate.Start < end - BoundaryTolerance && gateEnd > start + BoundaryTolerance)
            {
                destination.Add(
                    new(
                        gate.Ordinal,
                        gate.Epoch,
                        index / NotesPerTrack,
                        index % NotesPerTrack,
                        gate.Velocity,
                        gate.Start,
                        gateEnd,
                        gate.Quantize,
                        gate.MinimumDuration
                    )
                );
            }
        }

        return new(start, end, projectAligned, projectOffset, truncated);
    }

    public void Clear()
    {
        Array.Clear(completed);
        Array.Clear(active);
        first = 0;
        count = 0;
        activeCount = 0;
        epoch = 0;
        lostThrough = double.NegativeInfinity;
        discontinuity = true;
    }

    private void BeginEpoch(bool playing, double absoluteBeat)
    {
        epoch++;
        origin = beat;
        projectAligned = playing;
        projectOffset = beat - absoluteBeat;
        first = 0;
        count = 0;
        latestEnd = beat;
        lostThrough = double.NegativeInfinity;
        discontinuity = false;

        for (int index = 0; index < active.Length; index++)
        {
            if (active[index].Ordinal != 0)
            {
                active[index] = active[index] with { Epoch = epoch };
            }
        }
    }

    private void Add(PerformanceNote note)
    {
        if (count == MaximumNotes)
        {
            PerformanceNote evicted = completed[first];

            if (evicted.Epoch == epoch)
            {
                lostThrough = Math.Max(lostThrough, evicted.End);
            }

            first = (first + 1) % MaximumNotes;
            count--;
        }

        completed[(first + count) % MaximumNotes] = note;
        count++;
    }
}
