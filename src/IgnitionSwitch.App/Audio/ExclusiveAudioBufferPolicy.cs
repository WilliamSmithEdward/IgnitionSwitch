namespace IgnitionSwitch.App.Audio;

internal static class ExclusiveAudioBufferPolicy
{
    private const int MaximumRequestMilliseconds = 30;

    public static int PreferredMilliseconds(long minimumDevicePeriod)
    {
        return minimumDevicePeriod is <= 0
            or > (MaximumRequestMilliseconds * 10000L)
                ? throw new NotSupportedException(
                    "The output did not report a usable low-latency exclusive period."
                )
                : (int)Math.Max(1, Math.Ceiling(minimumDevicePeriod / 10000.0));
    }
}
