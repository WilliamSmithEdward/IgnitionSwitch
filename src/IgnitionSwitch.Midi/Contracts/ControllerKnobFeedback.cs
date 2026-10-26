namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Display text for one surface control; an empty name denotes an unused slot.</summary>
public sealed record ControllerKnobFeedback(string Name, string Value);
