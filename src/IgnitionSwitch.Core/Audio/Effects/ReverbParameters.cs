namespace IgnitionSwitch.Core.Audio.Effects;

internal readonly record struct ReverbParameters(
    float DecaySeconds,
    float Damping,
    float PreDelaySeconds,
    float ReturnLevel
);
