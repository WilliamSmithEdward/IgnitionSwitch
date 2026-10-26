namespace IgnitionSwitch.App.Audio;

internal static class SharedAudioBufferPolicy
{
    public const int CompatibilityBufferMilliseconds = 30;
    private const int DefaultPeriodMilliseconds = 10;
    private const double ReferenceTimesPerMillisecond = 10000;

    public static int PreferredMilliseconds(long sharedDefaultDevicePeriod)
    {
        if (sharedDefaultDevicePeriod <= 0)
        {
            return DefaultPeriodMilliseconds;
        }

        // GetDevicePeriod's default value describes shared mode; its minimum is exclusive-only.
        // Keep a positive timeout because NAudio also uses this request to wait for driver events.
        return (int)Math.Clamp(
            Math.Ceiling(sharedDefaultDevicePeriod / ReferenceTimesPerMillisecond),
            1,
            CompatibilityBufferMilliseconds
        );
    }
}
