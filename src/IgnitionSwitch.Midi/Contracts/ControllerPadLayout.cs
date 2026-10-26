namespace IgnitionSwitch.Midi.Contracts;

/// <summary>An immutable pad-note layout ordered left to right, starting at the top row.</summary>
public sealed class ControllerPadLayout
{
    /// <summary>Creates a rectangular layout containing distinct MIDI notes.</summary>
    public ControllerPadLayout(int columns, IReadOnlyList<int> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 128);
        int[] snapshot = [.. notes];

        if (
            snapshot.Length is < 1
                or > 128
                    || snapshot.Length % columns != 0
                    || snapshot.Any(note => note is < 0 or > 127)
                    || snapshot
                        .Distinct()
                        .Count()
                        != snapshot.Length
        )
        {
            throw new ArgumentException(
                "A pad layout must be rectangular and contain distinct MIDI notes.",
                nameof(notes)
            );
        }

        Columns = columns;
        Notes = Array.AsReadOnly(snapshot);
    }

    /// <summary>Number of pads in each physical row.</summary>
    public int Columns { get; }

    /// <summary>Detached, read-only notes in physical row order.</summary>
    public IReadOnlyList<int> Notes { get; }
}
