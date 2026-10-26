namespace IgnitionSwitch.Core.Domain;

/// <summary>The persistent notes owned by one clip.</summary>
/// <param name="SceneId">The owning stable scene slot.</param>
/// <param name="Notes">The clip's notes, at most 2048.</param>
public sealed record ClipSnapshot(int SceneId, IReadOnlyList<LoopNote> Notes);
