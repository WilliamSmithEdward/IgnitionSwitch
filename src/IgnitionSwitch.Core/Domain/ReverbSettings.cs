namespace IgnitionSwitch.Core.Domain;

/// <summary>Persistent normalized reverb targets.</summary>
/// <param name="Decay">Normalized decay time.</param>
/// <param name="Damping">High-frequency damping.</param>
/// <param name="PreDelay">Normalized pre-delay time.</param>
/// <param name="Return">Return gain.</param>
public sealed record ReverbSettings(float Decay, float Damping, float PreDelay, float Return);
