using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Persistence;

internal sealed record LegacyTrackDocument(
    string Name,
    InstrumentKind Instrument,
    bool IsMuted,
    float Volume,
    IReadOnlyList<LoopNote>? Notes
);
