using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;

namespace IgnitionSwitch.Midi.Protocol;

/// <summary>
/// Pure MK3 codec based on Novation's Programmer's Reference Guide, version 1, pages 4–17.
/// Device-specific messages must only be sent after identity verification.
/// </summary>
public static class LaunchkeyMk3Protocol
{
    /// <summary>Zero-based control-surface channel.</summary>
    public const int ControlChannel = 15;

    /// <summary>Previous track button CC.</summary>
    public const int TrackLeft = 103;

    /// <summary>Next track button CC.</summary>
    public const int TrackRight = 102;

    /// <summary>Capture MIDI button CC.</summary>
    public const int CaptureMidi = 74;

    /// <summary>Quantise button CC.</summary>
    public const int Quantise = 75;

    /// <summary>Metronome button CC.</summary>
    public const int Click = 76;

    /// <summary>Undo button CC.</summary>
    public const int Undo = 77;

    /// <summary>Play button CC.</summary>
    public const int Play = 115;

    /// <summary>Stop button CC.</summary>
    public const int Stop = 116;

    /// <summary>Record button CC.</summary>
    public const int Record = 117;

    /// <summary>Loop button CC.</summary>
    public const int Loop = 118;

    /// <summary>Pad mode change and report CC.</summary>
    public const int PadModeController = 3;

    /// <summary>Pot mode change and report CC.</summary>
    public const int PotModeController = 9;

    /// <summary>First of eight consecutive pot CC numbers.</summary>
    public const int FirstKnobController = 21;

    /// <summary>Scene Launch button CC on zero-based channel 0.</summary>
    public const int SceneLaunch = 104;

    /// <summary>Stop/Solo/Mute button CC on zero-based channel 0.</summary>
    public const int StopSoloMute = 105;

    /// <summary>Canonical scene-up button CC on the control channel; its legacy CC104 alias is ignored.</summary>
    public const int SceneUp = 106;

    /// <summary>Canonical scene-down button CC on the control channel; its legacy CC105 alias is ignored.</summary>
    public const int SceneDown = 107;

    /// <summary>A universal, manufacturer-independent device inquiry.</summary>
    public static byte[] IdentityInquiry()
    {
        return [0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7];
    }

    /// <summary>Enters or leaves the MK3 control-surface mode on its DAW output.</summary>
    public static byte[] DawMode(bool enabled)
    {
        return
        [
            0x9F,
            0x0C,
            enabled ? (byte)0x7F : (byte)0
        ];
    }

    /// <summary>Selects the session pad layout.</summary>
    public static byte[] SessionPadMode()
    {
        return [0xBF, 0x03, 0x02];
    }

    /// <summary>Selects the device pot layout.</summary>
    public static byte[] DevicePotMode()
    {
        return [0xBF, 0x09, 0x02];
    }

    /// <summary>Accepts a complete Novation 37 MK3 application identity, rejecting other models and bootloaders.</summary>
    public static bool TryReadIdentity(ReadOnlySpan<byte> data, out ControllerIdentity? identity)
    {
        identity = null;

        if (
            data.Length != 17
            || data[0] != 0xF0
            || data[1] != 0x7E
            || data[2] > 0x7F
            || data[3] != 0x06
            || data[4] != 0x02
            || data[5] != 0x00
            || data[6] != 0x20
            || data[7] != 0x29
            || data[8] != LaunchkeyMk3Profile.Model37.DeviceType
            || data[9] != 0x01
            || data[10] != 0
            || data[11] != 0
            || data[16] != 0xF7
        )
        {
            return false;
        }

        for (int i = 12; i < 16; i++)
        {
            if (data[i] > 9)
            {
                return false;
            }
        }

        identity = new(
            "Novation",
            LaunchkeyMk3Profile.Model37.ModelName,
            $"{data[12]}.{data[13]}.{data[14]}.{data[15]}"
        );
        return true;
    }

    /// <summary>Sets one LCD row (0 or 1), truncating to 16 characters and replacing non-ASCII with '?'.</summary>
    public static byte[] SetDisplayRow(int row, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, 1);
        return DisplayMessage(0x04, (byte)row, text);
    }

    /// <summary>Sets the LCD parameter name for a zero-based pot index.</summary>
    public static byte[] SetKnobName(int knob, string text)
    {
        return KnobMessage(0x07, knob, text);
    }

    /// <summary>Sets the LCD parameter value for a zero-based pot index.</summary>
    public static byte[] SetKnobValue(int knob, string text)
    {
        return KnobMessage(0x08, knob, text);
    }

    /// <summary>Relinquishes the host's default LCD contents.</summary>
    public static byte[] ClearDisplay()
    {
        return [0xF0, 0x00, 0x20, 0x29, 0x02, 0x0F, 0x06, 0xF7];
    }

    /// <summary>Session pad index is row-major: 0–7 at top, 8–15 at bottom. Color is a palette index.</summary>
    public static byte[] SetSessionPad(int pad, int color, bool pulse = false)
    {
        return SetSessionPad(
            pad,
            new LaunchkeyLedFeedback(
                color,
                pulse ? LaunchkeyLedMode.Pulsing : LaunchkeyLedMode.Steady
            )
        );
    }

    /// <summary>Sets a Session pad's palette color and steady, flashing, or pulsing state.</summary>
    public static byte[] SetSessionPad(int pad, LaunchkeyLedFeedback light)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pad);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pad, 15);
        return PaletteMessage(0x90, pad < 8 ? 96 + pad : 112 + pad - 8, light);
    }

    /// <summary>Sets an RGB CC-associated control's palette color; includes Scene Launch, mode, and navigation buttons.</summary>
    public static byte[] SetRgbControl(int controller, LaunchkeyLedFeedback light)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(controller);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(controller, 127);
        return PaletteMessage(0xB0, controller, light);
    }

    /// <summary>Colors a Drum-layout MIDI note from 36–51 using the MK3 palette on its DAW output.</summary>
    public static byte[] SetDrumPad(int note, int color, bool pulse = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(note, 36);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note, 51);
        ArgumentOutOfRangeException.ThrowIfNegative(color);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(color, 127);
        return
        [
            pulse ? (byte)0x9B : (byte)0x99,
            (byte)note,
            (byte)color
        ];
    }

    /// <summary>Colors a Device Select pad: top row 0–7 maps to notes 64–71, bottom 8–15 to 80–87.</summary>
    public static byte[] SetDeviceSelectPad(int pad, int color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pad);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pad, 15);
        ArgumentOutOfRangeException.ThrowIfNegative(color);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(color, 127);
        return
        [
            0x90,
            (byte)(pad < 8 ? 64 + pad : 80 + pad - 8),
            (byte)color
        ];
    }

    /// <summary>Sets the white Device Lock LED through the documented channel-16 grayscale CC.</summary>
    public static byte[] SetDeviceLock(bool locked)
    {
        return
        [
            0xBF,
            52,
            locked ? (byte)127 : (byte)0
        ];
    }

    private static byte[] KnobMessage(byte command, int knob, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(knob);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(knob, 7);
        return DisplayMessage(command, (byte)(0x38 + knob), text);
    }

    private static byte[] PaletteMessage(byte status, int number, LaunchkeyLedFeedback light)
    {
        return light.Color is < 0
            or > 127 || !Enum.IsDefined(light.Mode)
                ? throw new ArgumentOutOfRangeException(nameof(light))
                :
                [
                    (byte)(status + (int)light.Mode),
                    (byte)number,
                    (byte)light.Color
                ];
    }

    private static byte[] DisplayMessage(byte command, byte index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int length = Math.Min(text.Length, LaunchkeyMk3Profile.Model37.DisplayWidth);
        byte[] message = new byte[9 + length];
        ReadOnlySpan<byte> header = [0xF0, 0x00, 0x20, 0x29, 0x02, 0x0F];
        header.CopyTo(message);
        message[6] = command;
        message[7] = index;

        for (int i = 0; i < length; i++)
        {
            message[8 + i] = text[i] is >= ' ' and <= '~' ? (byte)text[i] : (byte)'?';
        }

        message[^1] = 0xF7;
        return message;
    }
}
