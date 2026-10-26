namespace IgnitionSwitch.Core.Sequencing.Performance;

internal readonly record struct PerformanceNote(
    long Ordinal,
    long Epoch,
    int Track,
    int Note,
    int Velocity,
    double Start,
    double End,
    bool Quantize,
    double MinimumDuration
);
