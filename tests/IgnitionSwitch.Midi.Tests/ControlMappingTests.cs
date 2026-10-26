using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;

internal static class ControlMappingTests
{
    public static void Commands()
    {
        var mapper = new LaunchkeyControlMapper();
        Check.That(
            mapper.TryMap(Cc(115, 127), out MusicControlEvent? command) && command?.Kind
                == MusicControlKind.TogglePlayback,
            "Play button did not map."
        );
        Check.That(!mapper.TryMap(Cc(115, 0), out _), "Button release repeated the command.");
        Check.That(
            !mapper.TryMap(new(MidiInputKind.ControlChange, 0, 115, 127, false), out _),
            "Wrong control channel produced a command."
        );
        Check.That(
            !mapper.TryMap(new(MidiInputKind.ControlChange, 15, 115, 127, true), out _),
            "Performance port produced a surface command."
        );
        Check.That(
            mapper.TryMap(Cc(103, 127), out command)
                && command is { Kind: MusicControlKind.MoveTrack, Index: -1 },
            "Track-left direction was incorrect."
        );
        Check.That(
            mapper.TryMap(Cc(102, 127), out command)
                && command is { Kind: MusicControlKind.MoveTrack, Index: 1 },
            "Track-right direction was incorrect."
        );
        Check.That(
            !mapper.TryMap(Cc(102, 0), out _) && !mapper.TryMap(Cc(103, 0), out _),
            "Releasing a track arrow must not move selection again."
        );
        Check.That(
            mapper.TryMap(new(MidiInputKind.NoteOn, 0, 99, 127, false), out command)
                && command is { Kind: MusicControlKind.SelectTrack, Index: 3 },
            "Session selection pad did not map."
        );
        Check.That(
            mapper.TryMap(new(MidiInputKind.NoteOn, 0, 112, 127, false), out command)
                && command is { Kind: MusicControlKind.ToggleTrackMute, Index: 0 },
            "Mute pad did not map."
        );
        Check.That(
            !mapper.TryMap(new(MidiInputKind.NoteOn, 9, 96, 127, false), out _),
            "Wrong pad channel produced a command."
        );
        Check.That(
            !mapper.TryMap(Cc(3, 9), out _) && mapper.PadMode == 9,
            "Pad mode report did not update state."
        );
        Check.That(
            !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 96, 127, false), out _),
            "Non-session layout produced a session command."
        );
        Check.That(
            !mapper.TryMap(Cc(3, 2), out _) && mapper.PadMode == 2,
            "Session mode did not restore."
        );
        Check.That(
            mapper.TryMap(Cc(21, 0), out command)
                && command is { Kind: MusicControlKind.SetTrackVolume, Index: 0, Value: 0 },
            "Zero gain was discarded as a button release."
        );
        Check.That(
            mapper.TryMap(Cc(24, 127), out command)
                && command is { Kind: MusicControlKind.SetTrackVolume, Index: 3, Value: 1 },
            "Maximum gain was not normalized."
        );
        Check.That(
            mapper.TryMap(Cc(25, 127), out command)
                && command is { Kind: MusicControlKind.SetMasterVolume, Value: 1 },
            "Master gain did not map."
        );
        Check.That(
            mapper.TryMap(Cc(26, 0), out command)
                && command is { Kind: MusicControlKind.SetTempo, Value: 60 },
            "Minimum tempo did not map."
        );
        Check.That(
            mapper.TryMap(Cc(26, 127), out command)
                && command is { Kind: MusicControlKind.SetTempo, Value: 180 },
            "Maximum tempo did not map."
        );
        Check.That(
            !mapper.TryMap(Cc(27, 127), out _) && !mapper.TryMap(Cc(28, 127), out _),
            "Unassigned pots produced commands."
        );
        Check.That(
            !mapper.TryMap(Cc(9, 3), out _) && mapper.PotMode == 3,
            "Pan mode report did not update state."
        );
        Check.That(!mapper.TryMap(Cc(21, 127), out _), "Pan mode incorrectly changed volume.");
        Check.That(
            !mapper.TryMap(Cc(9, 4), out _) && !mapper.TryMap(Cc(21, 127), out _),
            "Send mode incorrectly changed volume."
        );
        Check.That(
            !mapper.TryMap(Cc(9, 1), out _) && mapper.TryMap(Cc(21, 127), out _),
            "Volume mode was not supported."
        );
        Check.That(
            !mapper.TryMap(Cc(21, -1), out _) && !mapper.TryMap(Cc(21, 128), out _),
            "Unvalidated control value escaped normalization bounds."
        );
        ShiftUndo();
        PrimaryButton(75, MusicControlKind.QuantizeSelectedTrack, "Quantise");
        PrimaryButton(118, MusicControlKind.ToggleLoop, "Loop");
        PrimaryButton(74, MusicControlKind.CaptureRecentPerformance, "Capture MIDI");
    }

    private static void PrimaryButton(byte number, MusicControlKind kind, string name)
    {
        var mapper = new LaunchkeyControlMapper();
        Check.That(
            mapper.TryMap(Raw(0xBF, number, 127), out MusicControlEvent? command) && command?.Kind
                == kind,
            $"The raw {name} press must request its primary operation."
        );
        Check.That(
            !mapper.TryMap(Raw(0xBF, number, 0), out _),
            $"A {name} release must not repeat the operation."
        );
        Check.That(
            !mapper.TryMap(Raw(0xB0, number, 127), out _)
                && !mapper.TryMap(Raw(0xB1, number, 127), out _)
                && !mapper.TryMap(Raw(0xBF, number, 127, true), out _)
                && !mapper.TryMap(Cc(number, -1), out _)
                && !mapper.TryMap(Cc(number, 128), out _),
            $"{name} must reject wrong channels, performance input, and invalid values."
        );
        Check.That(
            !mapper.TryMap(Raw(0xB0, 108, 127), out _),
            "Shift must remain a modifier rather than an application command."
        );
        Check.That(
            mapper.TryMap(Raw(0xBF, number, 127), out command) && command?.Kind == kind,
            $"Shift+{name} must preserve the same primary operation."
        );
        Check.That(
            !mapper.TryMap(Raw(0xBF, number, 0), out _),
            $"Shift+{name} release must not repeat the operation."
        );
        AssertUndo(mapper, MusicControlKind.Redo, $"{name} must not clear the held Shift state.");
        Check.That(
            !mapper.TryMap(Raw(0xB0, 108, 0), out _),
            "Shift release must remain a modifier update."
        );
        Check.That(
            mapper.TryMap(Raw(0xBF, number, 1), out command) && command?.Kind == kind,
            $"A subsequent nonzero {name} press must retain its primary action."
        );
    }

    private static void ShiftUndo()
    {
        var mapper = new LaunchkeyControlMapper();
        AssertUndo(mapper, MusicControlKind.Undo, "Undo did not map without Shift.");
        Check.That(!mapper.TryMap(Cc(77, 0), out _), "Undo release repeated the command.");
        Check.That(
            !mapper.TryMap(Raw(0xB0, 0x6C, 127), out _),
            "Shift press produced an application command."
        );
        AssertUndo(mapper, MusicControlKind.Redo, "Raw DAW Shift+Undo did not map to Redo.");
        Check.That(
            !mapper.TryMap(Raw(0xB0, 0x6C, 0, true), out _)
                && !mapper.TryMap(Raw(0xB1, 0x6C, 0), out _)
                && !mapper.TryMap(Raw(0xBF, 0x6C, 0), out _),
            "Wrong-port or wrong-channel Shift release produced a command."
        );
        AssertUndo(mapper, MusicControlKind.Redo, "Unrelated Shift release cleared DAW Shift.");
        Check.That(
            !mapper.TryMap(Raw(0xB0, 0x6C, 0), out _),
            "Shift release produced an application command."
        );
        AssertUndo(mapper, MusicControlKind.Undo, "Shift stayed held after raw DAW release.");
        Check.That(
            !mapper.TryMap(Raw(0xB0, 0x6C, 127, true), out _)
                && !mapper.TryMap(Raw(0xB1, 0x6C, 127), out _)
                && !mapper.TryMap(Raw(0xBF, 0x6C, 127), out _),
            "Wrong-port or wrong-channel Shift press produced a command."
        );
        AssertUndo(mapper, MusicControlKind.Undo, "Unrelated Shift press changed Undo to Redo.");
        Check.That(
            !mapper.TryMap(Raw(0xBF, 77, 127, true), out _)
                && !mapper.TryMap(Raw(0xB0, 77, 127), out _),
            "Undo accepted a wrong port or channel."
        );
        Check.That(
            !mapper.TryMap(Raw(0xB0, 0x6C, 1), out _)
                && !mapper.TryMap(Cc(77, 0), out _)
                && !mapper.TryMap(Raw(0xB0, 0x6C, 0), out _),
            "Shift or Undo release produced a command."
        );
        AssertUndo(
            mapper,
            MusicControlKind.Undo,
            "Shift state stuck across a second press/release."
        );
    }

    private static void AssertUndo(
        LaunchkeyControlMapper mapper,
        MusicControlKind kind,
        string message
    )
    {
        Check.That(
            mapper.TryMap(Cc(77, 127), out MusicControlEvent? command) && command?.Kind == kind,
            message
        );
    }

    private static MidiInputEvent Raw(
        byte status,
        byte number,
        byte value,
        bool performance = false
    )
    {
        Check.That(
            MidiMessageParser.TryParse(
                [status, number, value],
                performance,
                out MidiInputEvent? input
            ),
            "Valid control packet was rejected."
        );
        return input ?? throw new InvalidOperationException("Parser returned no control message.");
    }

    private static MidiInputEvent Cc(int number, int value)
    {
        return new(MidiInputKind.ControlChange, 15, number, value, false);
    }
}
