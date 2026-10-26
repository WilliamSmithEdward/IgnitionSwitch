using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Protocol;

/// <summary>Pure validation and decoding for one complete MIDI message.</summary>
public static class MidiMessageParser
{
    /// <summary>Parses one complete MIDI message, rejecting malformed data and treating velocity-zero as note-off.</summary>
    public static bool TryParse(
        ReadOnlySpan<byte> data,
        bool sourcePerformance,
        out MidiInputEvent? input
    )
    {
        input = null;

        if (data.IsEmpty)
        {
            return false;
        }

        if (data.Length == 1)
        {
            MidiInputKind? realtime = data[0] switch
            {
                0xF8 => MidiInputKind.Clock,
                0xFA => MidiInputKind.Start,
                0xFB => MidiInputKind.Continue,
                0xFC => MidiInputKind.Stop,
                _ => null
            };

            if (realtime is { } kind)
            {
                input = new(kind, 0, 0, 0, sourcePerformance);
                return true;
            }

            return false;
        }

        int command = data[0] & 0xF0;
        int expectedLength = command is 0xC0 or 0xD0 ? 2 : 3;

        if (data.Length != expectedLength || data[0] < 0x80 || data[0] >= 0xF0)
        {
            return false;
        }

        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] >= 0x80)
            {
                return false;
            }
        }

        int channel = data[0] & 0x0F;
        byte number = data[1];
        byte value = data.Length == 3 ? data[2] : data[1];
        MidiInputKind? messageKind = command switch
        {
            0x80 => MidiInputKind.NoteOff,
            0x90 => value == 0 ? MidiInputKind.NoteOff : MidiInputKind.NoteOn,
            0xA0 => MidiInputKind.PolyphonicAftertouch,
            0xB0 => MidiInputKind.ControlChange,
            0xC0 => MidiInputKind.ProgramChange,
            0xD0 => MidiInputKind.ChannelAftertouch,
            0xE0 => MidiInputKind.PitchBend,
            _ => null
        };

        if (messageKind is not { } parsedKind)
        {
            return false;
        }

        input = new(
            parsedKind,
            channel,
            command is 0xD0 or 0xE0 ? 0 : number,
            command == 0xE0 ? data[1] | (data[2] << 7) : value,
            sourcePerformance
        );
        return true;
    }
}
