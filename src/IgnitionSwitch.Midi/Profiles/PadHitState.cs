using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Pure MK3 pad-hit visibility state. Callers serialize access and supply monotonic milliseconds.</summary>
public sealed class PadHitState
{
    /// <summary>A released hit remains visible for at least this many milliseconds after its onset.</summary>
    public const int MinimumHitMilliseconds = 80;
    private readonly bool[] supported = new bool[128];
    private readonly bool[] pressed = new bool[128];
    private readonly bool[] needsPaint = new bool[128];
    private readonly bool[] highlighted = new bool[128];
    private readonly long[] visibleUntil = new long[128];

    /// <summary>Creates activity state restricted to the given physical pad notes.</summary>
    public PadHitState(ControllerPadLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (int note in layout.Notes)
        {
            supported[note] = true;
        }
    }

    /// <summary>Coalesces a valid hit or release without allocating; invalid or unmapped data is ignored.</summary>
    public bool Update(PadFeedback feedback, long nowMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);

        if (
            feedback.Note is < 0
                or > 127 || !supported[feedback.Note] || feedback.Velocity is < 0
                or > 127
        )
        {
            return false;
        }

        bool held = feedback.IsPressed && feedback.Velocity > 0;
        pressed[feedback.Note] = held;

        if (held)
        {
            needsPaint[feedback.Note] = true;
            visibleUntil[feedback.Note] = nowMilliseconds > long.MaxValue - MinimumHitMilliseconds
                ? long.MaxValue
                : nowMilliseconds + MinimumHitMilliseconds;
        }

        return true;
    }

    /// <summary>Returns a Drum-mode palette color: white while active, otherwise red when muted or blue.</summary>
    public bool TryGetColor(
        int note,
        long nowMilliseconds,
        bool drumMode,
        bool muted,
        out int color
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);
        color = 0;

        if (note is < 0 or > 127 || !supported[note])
        {
            return false;
        }

        if (!drumMode)
        {
            needsPaint[note] = false;
            highlighted[note] = false;
            return false;
        }

        color = pressed[note] || needsPaint[note] || visibleUntil[note] > nowMilliseconds
            ? 3
            : muted ? 5 : 45;
        return true;
    }

    /// <summary>Acknowledges a painted frame, retaining its first visible highlight for at least 80 ms.</summary>
    public void MarkPainted(int note, bool isHighlighted, long nowMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);

        if (note is < 0 or > 127 || !supported[note])
        {
            return;
        }

        if (isHighlighted)
        {
            if (!highlighted[note] || needsPaint[note])
            {
                long deadline = nowMilliseconds > long.MaxValue - MinimumHitMilliseconds
                    ? long.MaxValue
                    : nowMilliseconds + MinimumHitMilliseconds;
                visibleUntil[note] = Math.Max(visibleUntil[note], deadline);
            }

            needsPaint[note] = false;
        }

        highlighted[note] = isHighlighted;
    }

    /// <summary>Whether a released hit still needs a future frame to restore its base color.</summary>
    public bool HasPendingRelease(long nowMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);

        for (int note = 0; note < supported.Length; note++)
        {
            if (
                !pressed[note]
                && (needsPaint[note] || highlighted[note] || visibleUntil[note] > nowMilliseconds)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Clears held and transient feedback for stop or teardown.</summary>
    public void Clear()
    {
        Array.Clear(pressed);
        Array.Clear(needsPaint);
        Array.Clear(highlighted);
        Array.Clear(visibleUntil);
    }
}
