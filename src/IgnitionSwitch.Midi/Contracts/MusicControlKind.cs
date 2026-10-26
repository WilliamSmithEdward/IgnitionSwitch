namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Application commands independent of a hardware manufacturer's MIDI assignments.</summary>
public enum MusicControlKind
{
    /// <summary>Toggles transport playback.</summary>
    TogglePlayback,
    /// <summary>Stops transport and releases held notes.</summary>
    Stop,
    /// <summary>Toggles loop recording.</summary>
    ToggleRecording,
    /// <summary>Toggles the metronome.</summary>
    ToggleMetronome,
    /// <summary>Selects the track at Index.</summary>
    SelectTrack,
    /// <summary>Moves track selection by Index (-1 or +1).</summary>
    MoveTrack,
    /// <summary>Toggles mute for the track at Index.</summary>
    ToggleTrackMute,
    /// <summary>Sets track Index to the normalized gain in Value.</summary>
    SetTrackVolume,
    /// <summary>Sets the master normalized gain in Value.</summary>
    SetMasterVolume,
    /// <summary>Sets the tempo in Value, expressed in beats per minute.</summary>
    SetTempo
}
