namespace IgnitionSwitch.Midi.Profiles;

/// <summary>MK3 RGB palette animation; timing follows the hardware's clock state.</summary>
public enum LaunchkeyLedMode
{
    /// <summary>Constant palette color.</summary>
    Steady,
    /// <summary>Alternates the palette color with its base color.</summary>
    Flashing,
    /// <summary>Pulses the palette color.</summary>
    Pulsing
}
