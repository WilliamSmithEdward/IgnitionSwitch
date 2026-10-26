using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Classifies verified MK3 drum-pad input as musical performance, retaining releases across layout changes.</summary>
public sealed class LaunchkeyPerformanceRouter
{
    private const int FirstDrumNote = 36;
    private const int LastDrumNote = 51;
    private readonly bool[] activeDrumPads = new bool[LastDrumNote - FirstDrumNote + 1];

    /// <summary>Passes performance input unchanged or normalizes control-port Drum notes. Calls must be serialized by the device session.</summary>
    public bool TryRoute(MidiInputEvent input, int padMode, out MidiInputEvent? performance)
    {
        ArgumentNullException.ThrowIfNull(input);
        performance = null;

        if (input.SourcePerformance)
        {
            performance = input;
            return true;
        }

        if (
            input.Channel != 9
            || input.Number is < FirstDrumNote
                or > LastDrumNote || input.Value is < 0
                or > 127 || input.Kind is not (MidiInputKind.NoteOn or MidiInputKind.NoteOff)
        )
        {
            return false;
        }

        int pad = input.Number - FirstDrumNote;
        bool release = input.Kind == MidiInputKind.NoteOff || input.Value == 0;

        if (padMode != 1 && (!release || !activeDrumPads[pad]))
        {
            return false;
        }

        activeDrumPads[pad] = !release;
        performance = new(
            release ? MidiInputKind.NoteOff : MidiInputKind.NoteOn,
            input.Channel,
            input.Number,
            input.Value,
            true
        );
        return true;
    }
}
