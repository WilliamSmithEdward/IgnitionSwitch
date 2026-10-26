namespace IgnitionSwitch.Core.Domain;

/// <summary>A read-only snapshot suitable for rendering a track in the interface.</summary>
/// <param name="Name">The displayed track name.</param>
/// <param name="Instrument">The instrument family.</param>
/// <param name="ColorHex">The track accent in RGB hex notation.</param>
/// <param name="NoteCount">The number of recorded MIDI notes in this track's clip in the current editing scene.</param>
/// <param name="IsMuted">Whether the track is muted.</param>
/// <param name="Volume">The track gain from zero to one.</param>
public sealed record TrackState(
    string Name,
    InstrumentKind Instrument,
    string ColorHex,
    int NoteCount,
    bool IsMuted,
    float Volume
);
