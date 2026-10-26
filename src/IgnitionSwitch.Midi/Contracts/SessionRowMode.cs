namespace IgnitionSwitch.Midi.Contracts;

/// <summary>The application-owned role of the lower Session pad row.</summary>
public enum SessionRowMode
{
    /// <summary>Launches clips from the next visible scene.</summary>
    Clips,
    /// <summary>Stops a track's clip while retaining the global transport.</summary>
    Stop,
    /// <summary>Toggles track solo.</summary>
    Solo,
    /// <summary>Toggles track mute.</summary>
    Mute
}
