namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Validated MIDI input; Channel is zero-based, and pitch Value is an unsigned 14-bit value.</summary>
public sealed class MidiInputEvent(
    MidiInputKind kind,
    int channel,
    int number,
    int value,
    bool sourcePerformance
)
{
    /// <summary>Kind of MIDI message.</summary>
    public MidiInputKind Kind { get; } = kind;

    /// <summary>Zero-based MIDI channel, or zero for system transport messages.</summary>
    public int Channel { get; } = channel;

    /// <summary>Note, controller, or program number. Zero for messages without a number.</summary>
    public int Number { get; } = number;

    /// <summary>Velocity, controller value, pressure, program, or unsigned 14-bit pitch bend.</summary>
    public int Value { get; } = value;

    /// <summary>True for the performance endpoint; false for the separate control-surface endpoint.</summary>
    public bool SourcePerformance { get; } = sourcePerformance;
}
