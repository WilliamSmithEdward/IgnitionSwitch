namespace IgnitionSwitch.Core.Audio.Effects;

internal static class EffectMath
{
    public static float Parameter(float value, float minimum, float maximum, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    public static float Input(float value)
    {
        return float.IsFinite(value) ? Flush(Math.Clamp(value, -8, 8)) : 0;
    }

    public static float Flush(float value)
    {
        return MathF.Abs(value) < 1e-12f ? 0 : value;
    }
}
