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
    SetTempo,
    /// <summary>Reverts the latest available project edit.</summary>
    Undo,
    /// <summary>Restores the latest reverted project edit.</summary>
    Redo,
    /// <summary>Aligns recorded notes on the selected track to the recording grid.</summary>
    QuantizeSelectedTrack,
    /// <summary>Toggles whether playback repeats at the loop boundary.</summary>
    ToggleLoop,
    /// <summary>Captures recently performed notes into the project.</summary>
    CaptureRecentPerformance,
    /// <summary>Sets absolute knob Index in ControlBank to the normalized Value, subject to pickup.</summary>
    SetKnob,
    /// <summary>Changes the active surface bank to Index.</summary>
    SelectControlBank,
    /// <summary>Moves the selected sound device by Index (-1 or +1).</summary>
    MoveSoundDevice,
    /// <summary>Selects parameter page Index of the current sound device.</summary>
    SelectParameterPage,
    /// <summary>Retains the actual sound device across track selection.</summary>
    ToggleSoundDeviceLock
}
