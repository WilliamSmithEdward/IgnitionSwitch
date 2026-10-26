using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Persistence;

internal sealed record SceneTrackDocument(
    string Name,
    InstrumentKind Instrument,
    bool IsMuted,
    float Volume,
    IReadOnlyList<ClipSnapshot> Clips,
    TrackSoundSettings Sound
);
