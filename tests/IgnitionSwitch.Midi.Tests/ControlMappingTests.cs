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
                && command is { Kind: MusicControlKind.TriggerSessionPad, Index: 3, Row: 0 },
            "Upper Session clip pad did not map."
        );
        Check.That(
            mapper.TryMap(new(MidiInputKind.NoteOn, 0, 112, 127, false), out command)
                && command is { Kind: MusicControlKind.TriggerSessionPad, Index: 0, Row: 1 },
            "Lower Session pad must retain its row for application-owned context."
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
            mapper.TryMap(Cc(21, 0), out command) && command?.Kind == MusicControlKind.SetKnob
                && command.Index == 0
                && command.Value == 0
                && command.ControlBank == ControllerControlBank.Device,
            "Zero parameter input was discarded as a button release."
        );
        Check.That(
            mapper.TryMap(Cc(24, 127), out command)
                && command is { Kind: MusicControlKind.SetKnob, Index: 3, Value: 1 },
            "Maximum parameter value was not normalized."
        );
        Check.That(
            mapper.TryMap(Cc(25, 127), out command)
                && command is { Kind: MusicControlKind.SetKnob, Index: 4, Value: 1 },
            "The fifth device parameter did not map."
        );
        Check.That(
            mapper.TryMap(Cc(26, 0), out command)
                && command is { Kind: MusicControlKind.SetKnob, Index: 5, Value: 0 },
            "Minimum device parameter did not map."
        );
        Check.That(
            mapper.TryMap(Cc(26, 127), out command)
                && command is { Kind: MusicControlKind.SetKnob, Index: 5, Value: 1 },
            "Maximum device parameter did not map."
        );
        Check.That(
            mapper.TryMap(Cc(27, 127), out command) && command?.Index == 6
                && mapper.TryMap(Cc(28, 127), out command)
                && command?.Index
                == 7,
            "The last two device parameters must be available."
        );
        Check.That(
            mapper.TryMap(Cc(9, 3), out command) && mapper.PotMode == 3 && command?.Kind
                == MusicControlKind.SelectControlBank
                && command.Index == (int)ControllerControlBank.Pan,
            "Pan mode report did not update state."
        );
        Check.That(
            mapper.TryMap(Cc(21, 127), out command) && command?.Kind == MusicControlKind.SetKnob
                && command.ControlBank == ControllerControlBank.Pan,
            "Pan mode must identify its own parameter bank."
        );
        Check.That(
            mapper.TryMap(Cc(9, 4), out _)
                && mapper.TryMap(Cc(21, 127), out command)
                && command?.ControlBank
                == ControllerControlBank.SendA,
            "Send mode must identify its own parameter bank."
        );
        Check.That(
            mapper.TryMap(Cc(9, 1), out _)
                && mapper.TryMap(Cc(21, 127), out command)
                && command?.ControlBank
                == ControllerControlBank.Volume,
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

    public static void SoundModes()
    {
        var mapper = new LaunchkeyControlMapper();
        ControllerControlBank[] banks = [
            ControllerControlBank.Volume, ControllerControlBank.Device, ControllerControlBank.Pan,
            ControllerControlBank.SendA, ControllerControlBank.SendB
        ];

        for (int mode = 1; mode <= 5; mode++)
        {
            Check.That(
                mapper.TryMap(Cc(9, mode), out MusicControlEvent? command)
                    && command is { Kind: MusicControlKind.SelectControlBank }
                    && command.Index == (int)banks[mode - 1],
                "A mode report must update the semantic bank."
            );

            for (int knob = 0; knob < 8; knob++)
            {
                foreach (int value in new[] { 0, 64, 127 })
                {
                    Check.That(
                        mapper.TryMap(Cc(21 + knob, value), out command) && command?.Kind
                            == MusicControlKind.SetKnob
                            && command.Index == knob
                            && command.ControlBank == banks[mode - 1]
                            && command.Value == value / 127.0,
                        "Every pot must retain its index, bank, and zero/full values."
                    );
                }
            }
        }

        foreach (int mode in new[] { 0, 6, 7, 8, 9, 127 })
        {
            Check.That(
                mapper.TryMap(Cc(9, mode), out MusicControlEvent? command) && command?.Index
                    == (int)ControllerControlBank.Custom
                    && !mapper.TryMap(Cc(21, 127), out _),
                "Custom and unknown modes must retain hardware ownership."
            );
        }

        _ = mapper.TryMap(Cc(3, 9), out _);
        Check.That(
            mapper.TryMap(Cc(106, 127), out MusicControlEvent? previous)
                && previous is { Kind: MusicControlKind.MoveSoundDevice, Index: -1 }
                && mapper.TryMap(Cc(107, 127), out MusicControlEvent? next)
                && next is { Kind: MusicControlKind.MoveSoundDevice, Index: 1 },
            "Device Select arrows must navigate devices in order."
        );

        for (int page = 0; page < 8; page++)
        {
            Check.That(
                mapper.TryMap(
                    new(MidiInputKind.NoteOn, 0, 64 + page, 127, false),
                    out MusicControlEvent? command
                )
                    && command is { Kind: MusicControlKind.SelectParameterPage }
                    && command.Index == page,
                "The top Device Select row must identify parameter pages."
            );
            Check.That(
                !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 80 + page, 127, false), out _)
                    && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 64 + page, 0, false), out _),
                "Undocumented lower-row roles and page releases must remain inactive."
            );
        }

        Check.That(
            mapper.TryMap(Cc(52, 127), out MusicControlEvent? locked) && locked?.Kind
                == MusicControlKind.ToggleSoundDeviceLock
                && !mapper.TryMap(Cc(52, 0), out _),
            "Device Lock must toggle once on press."
        );
        _ = mapper.TryMap(Cc(3, 1), out _);
        Check.That(
            !mapper.TryMap(Cc(106, 127), out _)
                && !mapper.TryMap(new(MidiInputKind.NoteOn, 0, 64, 127, false), out _),
            "Leaving Device Select must stop page/device navigation."
        );
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
