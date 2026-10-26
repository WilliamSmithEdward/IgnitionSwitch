using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing.History;

internal readonly record struct SceneNote(int Track, int Id, LoopNote Note);
