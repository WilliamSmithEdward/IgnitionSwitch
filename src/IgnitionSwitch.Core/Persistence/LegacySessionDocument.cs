namespace IgnitionSwitch.Core.Persistence;

internal sealed record LegacySessionDocument(
    int Version,
    double Tempo,
    int LoopBars,
    float MasterVolume,
    IReadOnlyList<LegacyTrackDocument?>? Tracks
);
