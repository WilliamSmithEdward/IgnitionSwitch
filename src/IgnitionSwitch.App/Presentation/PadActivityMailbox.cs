namespace IgnitionSwitch.App.Presentation;

internal sealed class PadActivityMailbox
{
    private const long PressedMask = 128;
    private readonly long[] states = new long[128];

    public void Record(int note, int velocity, bool pressed)
    {
        if (note is < 0 or > 127 || velocity is < 0 or > 127)
        {
            return;
        }

        if (!pressed || velocity == 0)
        {
            _ = Interlocked.And(ref states[note], ~PressedMask);
            return;
        }

        long previous;
        long next;

        do
        {
            previous = Volatile.Read(ref states[note]);
            next = unchecked((((previous >> 8) + 1) << 8) | PressedMask | (uint)velocity);
        }

        while (Interlocked.CompareExchange(ref states[note], next, previous) != previous);
    }

    public PadActivity Read(int note)
    {
        long state = Volatile.Read(ref states[note]);
        return new PadActivity(state >> 8, (int)(state & 127), (state & PressedMask) != 0);
    }

    public void Clear()
    {
        for (int note = 0; note < states.Length; note++)
        {
            _ = Interlocked.Exchange(ref states[note], 0);
        }
    }
}

internal readonly record struct PadActivity(long Sequence, int Velocity, bool IsPressed);
