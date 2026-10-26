namespace IgnitionSwitch.Midi.Contracts;

/// <summary>A musical pad's note, MIDI velocity, and current pressed state.</summary>
/// <param name="Note">MIDI note from zero through 127.</param>
/// <param name="Velocity">MIDI velocity from zero through 127; zero also releases the pad.</param>
/// <param name="IsPressed">Whether the pad remains held.</param>
public readonly record struct PadFeedback(int Note, int Velocity, bool IsPressed);
