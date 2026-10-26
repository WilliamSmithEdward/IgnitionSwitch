namespace IgnitionSwitch.Midi.Contracts;

/// <summary>A normalized application command from a hardware adapter.</summary>
public sealed class MusicControlEvent(
    MusicControlKind kind,
    int index = 0,
    double value = 0,
    ControllerControlBank controlBank = ControllerControlBank.Device
)
{
    /// <summary>The command to execute.</summary>
    public MusicControlKind Kind { get; } = kind;

    /// <summary>Zero-based target index, bank identity, or signed selection offset, according to Kind.</summary>
    public int Index { get; } = index;

    /// <summary>Normalized gain (0–1) or tempo in BPM; unused commands carry zero.</summary>
    public double Value { get; } = value;

    /// <summary>The bank that gives an absolute knob its meaning.</summary>
    public ControllerControlBank ControlBank { get; } = controlBank;
}
