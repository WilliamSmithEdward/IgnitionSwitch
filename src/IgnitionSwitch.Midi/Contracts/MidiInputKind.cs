namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Channel messages and transport events accepted by the input parser.</summary>
public enum MidiInputKind
{
    /// <summary>Starts a note with a nonzero velocity.</summary>
    NoteOn,
    /// <summary>Releases a note, including a velocity-zero note-on.</summary>
    NoteOff,
    /// <summary>Updates a controller value.</summary>
    ControlChange,
    /// <summary>Updates pitch bend; 8192 is the center value.</summary>
    PitchBend,
    /// <summary>Selects a program.</summary>
    ProgramChange,
    /// <summary>Updates pressure for one note.</summary>
    PolyphonicAftertouch,
    /// <summary>Updates pressure for the entire channel.</summary>
    ChannelAftertouch,
    /// <summary>MIDI timing clock.</summary>
    Clock,
    /// <summary>Starts playback from the beginning.</summary>
    Start,
    /// <summary>Resumes playback.</summary>
    Continue,
    /// <summary>Stops playback.</summary>
    Stop
}
