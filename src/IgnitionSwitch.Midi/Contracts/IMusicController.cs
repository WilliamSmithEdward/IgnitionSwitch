namespace IgnitionSwitch.Midi.Contracts;

/// <summary>A music controller boundary independent of its manufacturer's protocol.</summary>
public interface IMusicController : IDisposable
{
    /// <summary>Raised on a MIDI callback thread. Subscribers must dispatch UI work themselves.</summary>
    event EventHandler<MidiInputEvent>? InputReceived;

    /// <summary>Normalized controller commands raised on a callback thread. Dispatch UI work yourself.</summary>
    event EventHandler<MusicControlEvent>? ControlReceived;

    /// <summary>Raised on a background thread when connection state changes.</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>Whether a supported controller has completed its identity handshake.</summary>
    bool IsConnected { get; }

    /// <summary>Human-readable progress, connection state, or recovery instructions.</summary>
    string Status { get; }

    /// <summary>Verified identity while connected, otherwise null.</summary>
    ControllerIdentity? Identity { get; }

    /// <summary>Snapshot of all MIDI endpoints observed during the latest discovery.</summary>
    IReadOnlyList<MidiEndpointInfo> Endpoints { get; }

    /// <summary>Starts discovery in the background. Progress is reported through StatusChanged.</summary>
    void Connect();

    /// <summary>Closes the active connection and relinquishes hardware feedback.</summary>
    void Disconnect();

    /// <summary>Synchronizes track selection, mute flags, and transport feedback.</summary>
    void UpdateFeedback(ControllerFeedback feedback);
}
