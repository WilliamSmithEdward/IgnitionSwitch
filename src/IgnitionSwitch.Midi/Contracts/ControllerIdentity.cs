namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Identity verified by the controller's own hardware handshake.</summary>
public sealed record ControllerIdentity(string Manufacturer, string Model, string Firmware);
