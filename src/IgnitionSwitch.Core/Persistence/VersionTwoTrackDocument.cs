using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Persistence;

internal sealed record VersionTwoTrackDocument(
    string Name,
    InstrumentKind Instrument,
    bool IsMuted,
    float Volume,
    IReadOnlyList<LoopNote> Notes,
    TrackSoundSettings Sound
);
