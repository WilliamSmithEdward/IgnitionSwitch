namespace IgnitionSwitch.Core.Sequencing.Performance;

internal readonly record struct PerformanceGate(
    long Ordinal,
    long Epoch,
    int Velocity,
    double Start,
    bool Quantize,
    double MinimumDuration
);
