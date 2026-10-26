namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Bounded palette color and hardware animation mode for an MK3 RGB control.</summary>
public readonly record struct LaunchkeyLedFeedback(
    int Color,
    LaunchkeyLedMode Mode = LaunchkeyLedMode.Steady
);
