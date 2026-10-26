namespace IgnitionSwitch.Core.Sequencing.Performance;

internal readonly record struct PerformanceWindow(
    double Start,
    double End,
    bool ProjectAligned,
    double ProjectOffset,
    bool Truncated
);
