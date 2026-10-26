namespace IgnitionSwitch.App.Services;

// Access is serialized by StudioSession's input lock. These are received-input statistics,
// independent of audio availability, recording, rendered voices, and visual feedback.
internal sealed class PerformanceInputDiagnostics
{
    private long count;
    private int lastVelocity;
    private int minimumVelocity;
    private int maximumVelocity;

    public readonly record struct Snapshot(
        long DrumNoteOnCount,
        int LastDrumVelocity,
        int MinimumDrumVelocity,
        int MaximumDrumVelocity
    );

    public void RecordDrumNoteOn(int velocity)
    {
        if (velocity is < 1 or > 127)
        {
            return;
        }

        if (count < long.MaxValue)
        {
            count++;
        }

        lastVelocity = velocity;
        minimumVelocity = minimumVelocity == 0 ? velocity : Math.Min(minimumVelocity, velocity);
        maximumVelocity = Math.Max(maximumVelocity, velocity);
    }

    public Snapshot Read()
    {
        return new(count, lastVelocity, minimumVelocity, maximumVelocity);
    }

    public void Reset()
    {
        count = 0;
        lastVelocity = 0;
        minimumVelocity = 0;
        maximumVelocity = 0;
    }
}
