using IgnitionSwitch.App.Audio;

namespace IgnitionSwitch.App;

internal static class AudioBufferTests
{
    public static void SharedPeriod()
    {
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(100000) == 10,
            "A reported 10 ms shared period must replace the old fixed 30 ms request."
        );
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(26667) == 3,
            "Fractional shared periods must round up to the positive millisecond request supported by this output API."
        );
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(101000) == 11,
            "The request must not round a driver period down."
        );
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(1) == 1,
            "The driver event timeout must never become zero for a sub-millisecond report."
        );
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(0) == 10
                && SharedAudioBufferPolicy.PreferredMilliseconds(-1) == 10,
            "Missing or invalid timing data must retain a positive shared-mode default."
        );
        Check.That(
            SharedAudioBufferPolicy.PreferredMilliseconds(long.MaxValue)
                == SharedAudioBufferPolicy.CompatibilityBufferMilliseconds,
            "Unusually large timing values must stay bounded without integer overflow."
        );
        Check.That(
            SharedAudioBufferPolicy.CompatibilityBufferMilliseconds == 30,
            "The established 30 ms request must remain available for driver compatibility."
        );
    }
}
