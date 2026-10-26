using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

internal static class SessionControlMappingTests
{
    public static void Commands()
    {
        var mapper = new LaunchkeyControlMapper();

        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                int note = (row == 0 ? 96 : 112) + column;
                bool mapped = mapper.TryMap(
                    new(MidiInputKind.NoteOn, 0, note, 100, false),
                    out MusicControlEvent? command
                );
                Check.That(
                    mapped == (column < 4),
                    "Only four real track columns may produce Session commands."
                );

                if (mapped)
                {
                    Check.That(
                        command?.Kind == MusicControlKind.TriggerSessionPad
                            && command.Index == column
                            && command.Row == row,
                        "A Session pad must retain its track column and visible row."
                    );
                }

                Check.That(
                    !mapper.TryMap(new(MidiInputKind.NoteOff, 0, note, 100, false), out _)
                        && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, note, 0, false), out _)
                        && !mapper.TryMap(new(MidiInputKind.NoteOn, 1, note, 100, false), out _)
                        && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, note, 100, true), out _),
                    "Pad releases and wrong channel/port data must not launch clips."
                );
            }
        }

        AssertCommand(mapper, Cc(0, 104), MusicControlKind.LaunchSelectedScene);
        AssertCommand(mapper, Cc(0, 105), MusicControlKind.CycleSessionRowMode);
        AssertCommand(mapper, Cc(15, 106), MusicControlKind.MoveScene, -1);
        AssertCommand(mapper, Cc(15, 107), MusicControlKind.MoveScene, 1);

        foreach (MidiInputEvent input in new[]
        {
            Cc(15, 104),
            Cc(15, 105),
            Cc(0, 106),
            Cc(0, 107),
            Cc(1, 104),
            Cc(1, 105),
            Cc(15, 106, 0),
            Cc(15, 107, 0),
            Cc(0, 104, 0),
            Cc(0, 105, 0),
            new MidiInputEvent(MidiInputKind.ControlChange, 0, 104, 127, true)
        })
        {
            Check.That(
                !mapper.TryMap(input, out _),
                "Legacy navigation aliases, releases, and wrong channels must not duplicate Session commands."
            );
        }

        _ = mapper.TryMap(Cc(0, 108), out _);
        Check.That(
            !mapper.TryMap(Cc(0, 104), out _)
                && !mapper.TryMap(Cc(0, 105), out _)
                && !mapper.TryMap(Cc(15, 106), out _)
                && !mapper.TryMap(Cc(15, 107), out _)
                && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 96, 127, false), out _)
                && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 112, 127, false), out _),
            "Native Shift menu gestures must not launch, stop, or cycle Session controls."
        );
        AssertCommand(mapper, Cc(15, 118), MusicControlKind.ToggleLoop);
        AssertCommand(mapper, Cc(15, 77), MusicControlKind.Redo);
        _ = mapper.TryMap(Cc(0, 108, 0), out _);
        AssertCommand(mapper, Cc(0, 105), MusicControlKind.CycleSessionRowMode);

        foreach (int mode in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10, 127 })
        {
            _ = mapper.TryMap(Cc(15, 3, mode), out _);
            Check.That(
                !mapper.TryMap(Cc(0, 104), out _)
                    && !mapper.TryMap(Cc(0, 105), out _)
                    && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 96, 127, false), out _),
                "Native Drum/Chord/Custom/Device layouts must retain ownership of Session-like gestures."
            );

            if (mode == 9)
            {
                AssertCommand(mapper, Cc(15, 106), MusicControlKind.MoveSoundDevice, -1);
                AssertCommand(mapper, Cc(15, 107), MusicControlKind.MoveSoundDevice, 1);
            }

            else
            {
                Check.That(
                    !mapper.TryMap(Cc(15, 106), out _) && !mapper.TryMap(Cc(15, 107), out _),
                    "Scene navigation must be limited to Session mode."
                );
            }
        }

        _ = mapper.TryMap(Cc(15, 3, 2), out _);
        AssertCommand(mapper, Cc(0, 104), MusicControlKind.LaunchSelectedScene);
    }

    private static MidiInputEvent Cc(int channel, int number, int value = 127)
    {
        return new(MidiInputKind.ControlChange, channel, number, value, false);
    }

    private static void AssertCommand(
        LaunchkeyControlMapper mapper,
        MidiInputEvent input,
        MusicControlKind kind,
        int index = 0
    )
    {
        Check.That(
            mapper.TryMap(input, out MusicControlEvent? command) && command?.Kind == kind
                && command.Index == index,
            "A documented button press must produce exactly its contextual command."
        );
    }
}
