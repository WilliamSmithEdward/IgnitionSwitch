using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.App.Services;

internal sealed record SceneEditorSnapshot(
    int SceneId,
    int LoopBars,
    long NoteRevision,
    IReadOnlyList<TrackState> Tracks,
    IReadOnlyList<IReadOnlyList<LoopNote>> Notes
);
