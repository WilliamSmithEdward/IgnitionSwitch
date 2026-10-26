using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Devices;

namespace IgnitionSwitch.App.Services;

internal static class ControllerFactory
{
    public static IMusicController CreateDefault()
    {
        return new LaunchkeyController();
    }
}
