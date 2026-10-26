namespace IgnitionSwitch.Core.Domain;

/// <summary>An exact parameter target, independent of hardware modes or selected devices.</summary>
/// <param name="Id">The parameter identity.</param>
/// <param name="Track">Track zero through three for track parameters; minus one for global parameters.</param>
public readonly record struct ParameterAddress(ParameterId Id, int Track = -1);
