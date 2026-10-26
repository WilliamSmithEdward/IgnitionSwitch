using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing.History;

internal readonly record struct NoteEdit(
    int Track,
    int Id,
    int Index,
    LoopNote? Before,
    LoopNote? After,
    int SceneId = 0
)
{
    public bool HasChanges => Before != After;
}
