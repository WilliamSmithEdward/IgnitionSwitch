namespace IgnitionSwitch.Core.Domain;

internal static class ParameterCatalog
{
    private static readonly ParameterDescriptor[,] Descriptors = CreateDescriptors();

    public static bool IsTrack(ParameterId id)
    {
        return id is ParameterId.Tone
            or ParameterId.Attack
            or ParameterId.Release
            or ParameterId.Drive
            or ParameterId.Pan
            or ParameterId.SendA
            or ParameterId.SendB
            or ParameterId.Level
            or ParameterId.TrackMute;
    }

    public static ParameterDescriptor Get(
        ParameterId id,
        InstrumentKind instrument = InstrumentKind.Keys
    )
    {
        return Descriptors[(int)instrument, (int)id];
    }

    public static TrackSoundSettings TrackDefaults(InstrumentKind instrument)
    {
        return new(
            0.5f,
            Get(ParameterId.Attack, instrument).DefaultNormalized,
            Get(ParameterId.Release, instrument).DefaultNormalized,
            0,
            0.5f,
            0,
            0
        );
    }

    public static DelaySettings DelayDefaults =>
        new(
            Get(ParameterId.DelayTime).DefaultNormalized,
            Get(ParameterId.DelayFeedback).DefaultNormalized,
            0.5f,
            0.25f
        );
    public static ReverbSettings ReverbDefaults =>
        new(
            Get(ParameterId.ReverbDecay).DefaultNormalized,
            0.5f,
            Get(ParameterId.ReverbPreDelay).DefaultNormalized,
            0.25f
        );

    private static ParameterDescriptor[,] CreateDescriptors()
    {
        var values = new ParameterDescriptor[Enum.GetValues<InstrumentKind>().Length, Enum.GetValues<ParameterId>().Length];

        foreach (InstrumentKind instrument in Enum.GetValues<InstrumentKind>())
        {
            ParameterDescriptor[] row =
            [
                new(ParameterId.Tone, "Tone", "", -1, 1, 0),
                new(
                    ParameterId.Attack,
                    "Attack",
                    "s",
                    0.001,
                    1,
                    instrument == InstrumentKind.Atmosphere ? 0.08 : 0.004,
                    true
                ),
                instrument == InstrumentKind.Drums
                    ? new(ParameterId.Release, "Tail", "x", 0.25, 4, 1, true)
                    : new(
                        ParameterId.Release,
                        "Release",
                        "s",
                        0.02,
                        4,
                        instrument == InstrumentKind.Atmosphere ? 0.7 : 0.12,
                        true
                    ),
                new(ParameterId.Drive, "Drive", "", 0, 1, 0),
                new(ParameterId.Pan, "Pan", "", -1, 1, 0),
                new(ParameterId.SendA, "Delay send", "", 0, 1, 0),
                new(ParameterId.SendB, "Reverb send", "", 0, 1, 0),
                new(ParameterId.Level, "Level", "", 0, 1, 0.8),
                new(ParameterId.MasterVolume, "Master", "", 0, 1, 0.65),
                new(ParameterId.Tempo, "Tempo", "BPM", 60, 180, 100),
                new(ParameterId.TrackMute, "Mute", "", 0, 1, 0),
                new(ParameterId.DelayTime, "Time", "s", 0.04, 2, 0.25, true),
                new(ParameterId.DelayFeedback, "Feedback", "", 0, 0.85, 0.3),
                new(ParameterId.DelayDamping, "Damping", "", 0, 1, 0.5),
                new(ParameterId.DelayReturn, "Delay return", "", 0, 1, 0.25),
                new(ParameterId.ReverbDecay, "Decay", "s", 0.2, 8, 1.8, true),
                new(ParameterId.ReverbDamping, "Damping", "", 0, 1, 0.5),
                new(ParameterId.ReverbPreDelay, "Pre-delay", "s", 0, 0.25, 0.02),
                new(ParameterId.ReverbReturn, "Reverb return", "", 0, 1, 0.25)
            ];

            foreach (ParameterDescriptor descriptor in row)
            {
                values[(int)instrument, (int)descriptor.Id] = descriptor;
            }
        }

        return values;
    }
}
