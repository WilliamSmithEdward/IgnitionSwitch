namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Application state for hardware feedback; track names and mute flags must have equal lengths from 1–8.</summary>
public sealed record ControllerFeedback(
    int SelectedTrack,
    bool Playing,
    bool Recording,
    IReadOnlyList<string> TrackNames,
    IReadOnlyList<bool> TrackMuted,
    bool LoopEnabled = true,
    IReadOnlyList<ControllerKnobFeedback>? Knobs = null,
    string? DeviceName = null,
    bool DeviceLocked = false,
    ControllerSessionFeedback? Session = null
);
