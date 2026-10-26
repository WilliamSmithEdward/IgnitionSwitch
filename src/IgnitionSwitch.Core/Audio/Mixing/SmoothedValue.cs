namespace IgnitionSwitch.Core.Audio.Mixing;

internal sealed class SmoothedValue(float initial)
{
    private const int RampFrames = 240;
    private int remaining;
    private float step;

    public float Target { get; private set; } = initial;
    public float Current { get; private set; } = initial;

    public void SetTarget(float value, bool immediate)
    {
        if (immediate)
        {
            Target = value;
            Current = value;
            remaining = 0;
        }

        else if (Target != value)
        {
            Target = value;
            step = (value - Current) / RampFrames;
            remaining = RampFrames;
        }
    }

    public float Next()
    {
        if (remaining > 0)
        {
            remaining--;
            Current = remaining == 0 ? Target : Current + step;
        }

        return Current;
    }
}
