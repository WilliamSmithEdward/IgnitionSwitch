using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

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
    }

    private static MidiInputEvent Cc(int number, int value)
    {
        return new(MidiInputKind.ControlChange, 15, number, value, false);
    }
}
