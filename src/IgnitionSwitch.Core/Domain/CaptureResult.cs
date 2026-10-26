namespace IgnitionSwitch.Core.Domain;

/// <summary>The outcome and unique candidate-note count of a retrospective capture.</summary>
/// <param name="Status">Whether capture changed content or why it did not.</param>
/// <param name="NoteCount">Merged candidate count for Captured or NoChanges; zero for other outcomes.</param>
public sealed record CaptureResult(CaptureStatus Status, int NoteCount);
