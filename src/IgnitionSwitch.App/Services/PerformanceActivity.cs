namespace IgnitionSwitch.App.Services;

internal readonly record struct PerformanceActivity(
    int Track,
    int Note,
    int Velocity,
    bool IsPressed
);
