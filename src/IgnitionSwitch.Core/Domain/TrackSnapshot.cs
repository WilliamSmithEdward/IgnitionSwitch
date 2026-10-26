namespace IgnitionSwitch.Core.Domain;

/// <summary>A track's persistent musical content and mixer state.</summary>
/// <param name="Name">The displayed track name, up to 32 characters.</param>
/// <param name="Instrument">The synthesized instrument family.</param>
/// <param name="IsMuted">Whether the track is muted.</param>
/// <param name="Volume">The track gain from zero to one.</param>
/// <param name="Notes">The recorded loop notes, at most 2048.</param>
public sealed record TrackSnapshot(
    string Name,
    InstrumentKind Instrument,
    bool IsMuted,
    float Volume,
    IReadOnlyList<LoopNote> Notes
);
