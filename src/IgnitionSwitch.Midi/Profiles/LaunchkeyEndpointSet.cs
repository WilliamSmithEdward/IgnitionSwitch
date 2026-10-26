using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>A same-device set of performance ports and optional control-surface ports.</summary>
public sealed record LaunchkeyEndpointSet(
    MidiEndpointInfo PerformanceInput,
    MidiEndpointInfo PerformanceOutput,
    MidiEndpointInfo? DawInput,
    MidiEndpointInfo? DawOutput
);
