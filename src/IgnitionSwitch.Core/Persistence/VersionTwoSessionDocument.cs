using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Persistence;

internal sealed record VersionTwoSessionDocument(
    int Version,
    double Tempo,
    int LoopBars,
    float MasterVolume,
    IReadOnlyList<VersionTwoTrackDocument> Tracks,
    DelaySettings Delay,
    ReverbSettings Reverb
);
