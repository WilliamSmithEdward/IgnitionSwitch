namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Visible clip-slot state; Exists distinguishes a real slot from an unavailable scene.</summary>
public readonly record struct ControllerClipFeedback(
    bool Exists,
    bool HasNotes,
    bool Playing,
    bool Queued,
    bool Recording,
    bool StopQueued
);
