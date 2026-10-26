using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Persistence;

internal sealed record SceneSessionDocument(
    int Version,
    double Tempo,
    int LoopBars,
    float MasterVolume,
    IReadOnlyList<SceneTrackDocument> Tracks,
    DelaySettings Delay,
    ReverbSettings Reverb,
    IReadOnlyList<SceneState> Scenes
);
