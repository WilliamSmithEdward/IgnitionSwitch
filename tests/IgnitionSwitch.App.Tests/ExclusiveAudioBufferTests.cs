using IgnitionSwitch.App.Audio;

namespace IgnitionSwitch.App;

internal static class ExclusiveAudioBufferTests
{
    public static void MinimumPeriod()
    {
        Check.That(
            ExclusiveAudioBufferPolicy.PreferredMilliseconds(30000) == 3,
            "The exclusive path must request the reported three-millisecond minimum."
        );
        Check.That(
            ExclusiveAudioBufferPolicy.PreferredMilliseconds(26667) == 3
                && ExclusiveAudioBufferPolicy.PreferredMilliseconds(30001) == 4,
            "Exclusive periods must round up rather than request an unsupported shorter interval."
        );
        Check.That(
            ExclusiveAudioBufferPolicy.PreferredMilliseconds(1) == 1,
            "Event-driven exclusive output must retain a positive millisecond request."
        );
        Check.That(
            ExclusiveAudioBufferPolicy.PreferredMilliseconds(300000) == 30,
            "The exclusive request must remain within its bounded compatibility ceiling."
        );
        Check.Throws<NotSupportedException>(
            () => ExclusiveAudioBufferPolicy.PreferredMilliseconds(0)
        );
        Check.Throws<NotSupportedException>(
            () => ExclusiveAudioBufferPolicy.PreferredMilliseconds(-1)
        );
        Check.Throws<NotSupportedException>(
            () => ExclusiveAudioBufferPolicy.PreferredMilliseconds(300001)
        );
        Check.Throws<NotSupportedException>(
            () => ExclusiveAudioBufferPolicy.PreferredMilliseconds(long.MaxValue)
        );
    }
}
