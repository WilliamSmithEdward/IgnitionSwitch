namespace IgnitionSwitch.Core.Audio.Mixing;

internal sealed class SmoothedValue(float initial)
{
    private LinearRamp ramp = new(initial);

    public float Target => ramp.Target;
    public float Current => ramp.Current;

    public void SetTarget(float value, bool immediate)
    {
        ramp.SetTarget(value, immediate);
    }

    public float Next()
    {
        return ramp.Next();
    }
}
