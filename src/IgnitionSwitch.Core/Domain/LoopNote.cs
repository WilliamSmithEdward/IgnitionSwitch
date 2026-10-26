namespace IgnitionSwitch.Core.Domain;

/// <summary>A MIDI note expressed in loop-relative musical beats.</summary>
/// <param name="Note">The MIDI note number from zero to 127.</param>
/// <param name="Velocity">The note velocity from one to 127.</param>
/// <param name="Beat">The onset from zero to less than sixteen beats.</param>
/// <param name="Duration">The positive duration, at most sixteen beats; it may cross the loop boundary.</param>
public sealed record LoopNote(int Note, int Velocity, double Beat, double Duration);
