namespace IgnitionSwitch.Core.Audio.Effects;

internal readonly record struct DelayParameters(
    float TimeSeconds,
    float Feedback,
    float Damping,
    float ReturnLevel
);
