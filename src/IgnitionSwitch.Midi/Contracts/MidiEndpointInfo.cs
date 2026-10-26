namespace IgnitionSwitch.Midi.Contracts;

/// <summary>One Windows MIDI endpoint; candidate flags are hints, never a verified device identity.</summary>
public sealed record MidiEndpointInfo(
    string Id,
    string Name,
    MidiEndpointDirection Direction,
    string? ContainerId,
    bool IsLaunchkeyCandidate,
    bool IsDaw
);
