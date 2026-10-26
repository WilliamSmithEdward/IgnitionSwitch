using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing.History;

internal sealed record SceneEdit(
    int SceneId,
    int Index,
    SceneState? Before,
    SceneState? After,
    IReadOnlyList<SceneNote> Notes
);
