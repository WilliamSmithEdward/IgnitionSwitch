using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Protocol;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Maps verified MK3 surface input to IgnitionSwitch's four-track controls, respecting the current hardware modes.</summary>
public sealed class LaunchkeyControlMapper
{
    // The DAW port reports Shift on channel 1, independently of channel-16 commands.
    private const int ShiftController = 108;
    private bool shiftHeld;

    /// <summary>The current hardware pad mode. Session is 2.</summary>
    public int PadMode { get; private set; } = 2;

    /// <summary>The current hardware pot mode. Device is 2; Volume is 1.</summary>
    public int PotMode { get; private set; } = 2;

    /// <summary>Maps one control message, or updates mode state. Performance data and unsupported modes produce no command.</summary>
    public bool TryMap(MidiInputEvent input, out MusicControlEvent? control)
    {
        ArgumentNullException.ThrowIfNull(input);
        control = null;

        if (
            input.SourcePerformance
            || input.Channel is < 0
                or > 15 || input.Number is < 0
                or > 127 || input.Value is < 0
                or > 127
        )
        {
            return false;
        }

        if (
            input.Kind == MidiInputKind.ControlChange
            && input.Channel == 0
            && input.Number == ShiftController
        )
        {
            shiftHeld = input.Value > 0;
            return false;
        }

        if (
            input.Kind == MidiInputKind.NoteOn
            && input.Channel == 0
            && input.Value > 0
            && PadMode == 9
            && input.Number is >= 64 and <= 71
        )
        {
            control = new(MusicControlKind.SelectParameterPage, input.Number - 64);
            return true;
        }

        if (
            input.Kind == MidiInputKind.NoteOn
            && input.Channel == 0
            && input.Value > 0
            && PadMode == 2
        )
        {
            control = input.Number switch
            {
                >= 96 and <= 99 => new(MusicControlKind.SelectTrack, input.Number - 96),
                >= 112 and <= 115 => new(MusicControlKind.ToggleTrackMute, input.Number - 112),
                _ => null
            };
            return control is not null;
        }

        if (
            input.Kind != MidiInputKind.ControlChange
            || input.Channel != LaunchkeyMk3Protocol.ControlChannel
        )
        {
            return false;
        }

        if (input.Number == LaunchkeyMk3Protocol.PadModeController)
        {
            PadMode = input.Value;
            return false;
        }

        if (input.Number == LaunchkeyMk3Protocol.PotModeController)
        {
            PotMode = input.Value;
            control = new(MusicControlKind.SelectControlBank, (int)GetControlBank(PotMode));
            return true;
        }

        if (input.Number is >= 21 and <= 28)
        {
            if (PotMode is not (>= 1 and <= 5))
            {
                return false;
            }

            control = new(
                MusicControlKind.SetKnob,
                input.Number - 21,
                input.Value / 127.0,
                GetControlBank(PotMode)
            );
            return true;
        }

        if (input.Value == 0)
        {
            return false;
        }

        control = input.Number switch
        {
            106 when PadMode == 9 => new(MusicControlKind.MoveSoundDevice, -1),
            107 when PadMode == 9 => new(MusicControlKind.MoveSoundDevice, 1),
            52 => new(MusicControlKind.ToggleSoundDeviceLock),
            LaunchkeyMk3Protocol.Play => new(MusicControlKind.TogglePlayback),
            LaunchkeyMk3Protocol.Stop => new(MusicControlKind.Stop),
            LaunchkeyMk3Protocol.Record => new(MusicControlKind.ToggleRecording),
            LaunchkeyMk3Protocol.Loop => new(MusicControlKind.ToggleLoop),
            LaunchkeyMk3Protocol.Click => new(MusicControlKind.ToggleMetronome),
            LaunchkeyMk3Protocol.Quantise => new(MusicControlKind.QuantizeSelectedTrack),
            LaunchkeyMk3Protocol.CaptureMidi => new(MusicControlKind.CaptureRecentPerformance),
            LaunchkeyMk3Protocol.Undo => new(
                shiftHeld ? MusicControlKind.Redo : MusicControlKind.Undo
            ),
            LaunchkeyMk3Protocol.TrackLeft => new(MusicControlKind.MoveTrack, -1),
            LaunchkeyMk3Protocol.TrackRight => new(MusicControlKind.MoveTrack, 1),
            _ => null
        };
        return control is not null;
    }

    private static ControllerControlBank GetControlBank(int mode)
    {
        return mode switch
        {
            1 => ControllerControlBank.Volume,
            2 => ControllerControlBank.Device,
            3 => ControllerControlBank.Pan,
            4 => ControllerControlBank.SendA,
            5 => ControllerControlBank.SendB,
            _ => ControllerControlBank.Custom
        };
    }
}
