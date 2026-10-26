namespace IgnitionSwitch.Core.Domain;

/// <summary>A detached clip content summary.</summary>
/// <param name="Address">The clip's owning track and scene.</param>
/// <param name="NoteCount">The number of stored notes.</param>
public readonly record struct ClipState(ClipAddress Address, int NoteCount);
