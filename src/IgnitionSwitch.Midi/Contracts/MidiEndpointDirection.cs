namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Direction from the application's perspective.</summary>
public enum MidiEndpointDirection
{
    /// <summary>Receives messages from the hardware.</summary>
    Input,
    /// <summary>Sends messages to the hardware.</summary>
    Output
}
