using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.App.Services;

internal enum SoundDeviceKind
{
    Instrument,
    Delay,
    Reverb
}

internal sealed record SoundParameterSlot(
    ParameterAddress Address,
    string Name,
    string Value,
    double Normalized
);

internal sealed record SoundControlPage(
    SoundDeviceKind Device,
    string Name,
    bool Locked,
    IReadOnlyList<SoundParameterSlot> Parameters
);
