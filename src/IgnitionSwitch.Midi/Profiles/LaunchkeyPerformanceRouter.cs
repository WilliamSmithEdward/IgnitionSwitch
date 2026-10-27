using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Classifies default-channel MK3 drum notes and owned pad pressure as musical performance across layout changes.</summary>
public sealed class LaunchkeyPerformanceRouter
{
    private const int FirstDrumNote = 36;
    private const int LastDrumNote = 51;
    private readonly bool[] activeDrumPads = new bool[LastDrumNote - FirstDrumNote + 1];
    private int activeDrumPadCount;

    /// <summary>Passes performance input unchanged or normalizes control-port Drum notes/owned pressure. Calls must be serialized by the device session.</summary>
    public bool TryRoute(MidiInputEvent input, int padMode, out MidiInputEvent? performance)
    {
        ArgumentNullException.ThrowIfNull(input);
        performance = null;

        if (input.SourcePerformance)
        {
            performance = input;
            return true;
        }

        if (input.Channel != 9 || input.Value is < 0 or > 127)
        {
            return false;
        }

        MidiInputKind kind = input.Kind;

        switch (kind)
        {
            case MidiInputKind.ChannelAftertouch when input.Number == 0 && activeDrumPadCount > 0:
            case MidiInputKind.PolyphonicAftertouch when input.Number is >= FirstDrumNote
                and <= LastDrumNote && activeDrumPads[input.Number - FirstDrumNote]:
                break;

            case MidiInputKind.NoteOn:
            case MidiInputKind.NoteOff:
                if (input.Number is < FirstDrumNote or > LastDrumNote)
                {
                    return false;
                }

                int pad = input.Number - FirstDrumNote;
                bool release = kind == MidiInputKind.NoteOff || input.Value == 0;

                if (padMode != 1 && (!release || !activeDrumPads[pad]))
                {
                    return false;
                }

                bool held = !release;

                if (activeDrumPads[pad] != held)
                {
                    activeDrumPads[pad] = held;
                    activeDrumPadCount += held ? 1 : -1;
                }

                kind = release ? MidiInputKind.NoteOff : MidiInputKind.NoteOn;
                break;

            case MidiInputKind.ControlChange:
            case MidiInputKind.PitchBend:
            case MidiInputKind.ProgramChange:
            case MidiInputKind.PolyphonicAftertouch:
            case MidiInputKind.ChannelAftertouch:
            case MidiInputKind.Clock:
            case MidiInputKind.Start:
            case MidiInputKind.Continue:
            case MidiInputKind.Stop:
            default:
                return false;
        }

        performance = new(kind, input.Channel, input.Number, input.Value, true);
        return true;
    }
}
