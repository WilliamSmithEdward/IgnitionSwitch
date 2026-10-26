using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Hardware capabilities for the first supported model. Other models need their own verified profile.</summary>
public sealed class LaunchkeyMk3Profile
{
    /// <summary>The first supported hardware model.</summary>
    public static LaunchkeyMk3Profile Model37 { get; } = new();

    private LaunchkeyMk3Profile() { }

    /// <summary>Human-readable model name.</summary>
    public string ModelName { get; } = "Launchkey 37 MK3";

    /// <summary>Novation device type returned by Universal Device Inquiry.</summary>
    public byte DeviceType { get; } = 0x35;

    /// <summary>Number of full-size keys.</summary>
    public int KeyCount { get; } = 37;

    /// <summary>Number of velocity-sensitive pads.</summary>
    public int PadCount { get; } = 16;

    /// <summary>The MK3's physical two-by-eight Drum note order, with the top row first.</summary>
    public ControllerPadLayout DrumPadLayout { get; } = new(
        8,
        [40, 41, 42, 43, 48, 49, 50, 51, 36, 37, 38, 39, 44, 45, 46, 47]
    );

    /// <summary>Number of hardware pots.</summary>
    public int KnobCount { get; } = 8;

    /// <summary>Characters per LCD row.</summary>
    public int DisplayWidth { get; } = 16;

    /// <summary>Finds possible full-size Launchkey endpoints. Identity must still be verified.</summary>
    public static bool IsCandidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return (name.Contains("Launchkey", StringComparison.OrdinalIgnoreCase)
            || name.Contains("LKMK3", StringComparison.OrdinalIgnoreCase))
            && !name.Contains("Mini", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("MK4", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recognizes known Windows names for the second, control-surface MIDI interface.</summary>
    public static bool IsDawName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Contains("DAW", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MIDIIN2", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MIDIOUT2", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Port 2", StringComparison.OrdinalIgnoreCase);
    }
}
