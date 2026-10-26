namespace IgnitionSwitch.Core.Domain;

/// <summary>Persistent normalized delay targets.</summary>
/// <param name="Time">Normalized delay time.</param>
/// <param name="Feedback">Normalized feedback.</param>
/// <param name="Damping">High-frequency damping.</param>
/// <param name="Return">Return gain.</param>
public sealed record DelaySettings(float Time, float Feedback, float Damping, float Return);
