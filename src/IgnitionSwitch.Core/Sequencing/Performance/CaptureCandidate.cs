using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing.Performance;

internal readonly record struct CaptureCandidate(
    int Track,
    long Ordinal,
    int Note,
    int Velocity,
    double Beat,
    double Duration
)
{
    public bool Matches(LoopNote note)
    {
        return Note == note.Note
            && Velocity == note.Velocity
            && Beat == note.Beat
            && Duration == note.Duration;
    }

    public LoopNote ToNote()
    {
        return new(Note, Velocity, Beat, Duration);
    }
}
