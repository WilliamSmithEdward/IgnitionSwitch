namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class SmoothedCoefficient(float initial, float smoothing)
{
    private float current = initial;
    private float target = initial;

    public void SetTarget(float value)
    {
        target = value;
    }

    public float Next()
    {
        float difference = target - current;
        current = MathF.Abs(difference) < 1e-6f
            ? target
            : EffectMath.Flush(current + (difference * smoothing));
        return current;
    }

    public void Reset()
    {
        current = target;
    }
}
