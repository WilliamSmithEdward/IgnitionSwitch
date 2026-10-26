using System.Collections.ObjectModel;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession
{
    private static readonly ParameterId[] InstrumentParameters = [
        ParameterId.Tone, ParameterId.Attack, ParameterId.Release, ParameterId.Drive,
        ParameterId.Pan, ParameterId.SendA, ParameterId.SendB, ParameterId.Level
    ];
    private SoundDeviceKind soundDevice;
    private bool soundDeviceLocked;
    private int lockedSoundTrack;
    private ControllerControlBank controlBank = ControllerControlBank.Device;
    private readonly AbsoluteKnobPickup knobPickup = new();

    public SoundControlPage GetSoundControlPage()
    {
        lock (inputLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int track = soundDeviceLocked ? lockedSoundTrack : Engine.SelectedTrack;
            var slots = new SoundParameterSlot[8];

            for (int index = 0; index < slots.Length; index++)
            {
                ParameterAddress address = GetSoundAddress(index, track);
                ParameterDescriptor descriptor = Engine.GetDescriptor(address);
                double normalized = Engine.GetParameter(address);
                string name = index >= 4 && soundDevice != SoundDeviceKind.Instrument
                    ? Engine.Tracks[index - 4].Name
                    : descriptor.Name;
                slots[index] = new(address, name, descriptor.Format(normalized), normalized);
            }

            string title = soundDevice switch
            {
                SoundDeviceKind.Delay => "Echo",
                SoundDeviceKind.Reverb => "Room",
                SoundDeviceKind.Instrument => Engine.Tracks[track].Name,
                _ => Engine.Tracks[track].Name
            };
            return new(soundDevice, title, soundDeviceLocked, Array.AsReadOnly(slots));
        }
    }

    public void SelectSoundDevice(SoundDeviceKind device)
    {
        if (!Enum.IsDefined(device))
        {
            throw new ArgumentOutOfRangeException(nameof(device));
        }

        lock (inputLock)
        {
            if (disposed || interactionBlocked || soundDevice == device)
            {
                return;
            }

            SelectSoundDeviceCore(device);
        }

        RefreshFeedback();
    }

    public void ToggleSoundDeviceLock()
    {
        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return;
            }

            ToggleSoundDeviceLockCore();
        }

        RefreshFeedback();
    }

    public bool SetParameter(ParameterAddress address, double normalized)
    {
        bool changed;

        lock (inputLock)
        {
            if (disposed || interactionBlocked)
            {
                return false;
            }

            changed = SetParameterCore(address, normalized);
        }

        if (changed)
        {
            RefreshFeedback();
        }

        return changed;
    }

    public void CompleteParameterGesture()
    {
        lock (inputLock)
        {
            if (!disposed)
            {
                Engine.CommitEdit();
            }
        }
    }

    private bool SetParameterCore(ParameterAddress address, double normalized)
    {
        double previous = Engine.GetParameter(address);
        Engine.SetParameter(address, normalized);
        bool changed = previous != Engine.GetParameter(address);

        if (changed)
        {
            revision++;
        }

        return changed;
    }

    private void SelectSoundDeviceCore(SoundDeviceKind device)
    {
        if (soundDevice == device)
        {
            return;
        }

        Engine.CommitEdit();
        soundDevice = device;

        if (soundDeviceLocked)
        {
            lockedSoundTrack = Engine.SelectedTrack;
        }

        knobPickup.Invalidate();
    }

    private void ToggleSoundDeviceLockCore()
    {
        Engine.CommitEdit();
        soundDeviceLocked = !soundDeviceLocked;

        if (soundDeviceLocked)
        {
            lockedSoundTrack = Engine.SelectedTrack;
        }

        knobPickup.Invalidate();
    }

    private ParameterAddress GetSoundAddress(int index, int track)
    {
        return soundDevice switch
        {
            SoundDeviceKind.Delay => index switch
            {
                0 => new(ParameterId.DelayTime),
                1 => new(ParameterId.DelayFeedback),
                2 => new(ParameterId.DelayDamping),
                3 => new(ParameterId.DelayReturn),
                _ => new(ParameterId.SendA, index - 4)
            },
            SoundDeviceKind.Reverb => index switch
            {
                0 => new(ParameterId.ReverbDecay),
                1 => new(ParameterId.ReverbDamping),
                2 => new(ParameterId.ReverbPreDelay),
                3 => new(ParameterId.ReverbReturn),
                _ => new(ParameterId.SendB, index - 4)
            },
            SoundDeviceKind.Instrument => new(InstrumentParameters[index], track),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    private void ProcessKnob(MusicControlEvent control)
    {
        if (
            control.Index is < 0
                or > 7 || !Enum.IsDefined(control.ControlBank) || control.Value is < 0
                or > 1
        )
        {
            return;
        }

        SelectControlBankCore(control.ControlBank);
        ParameterAddress? address = GetKnobAddress(control.Index);

        if (address is not { } target)
        {
            knobPickup.Observe(control.Index, control.Value);
            return;
        }

        double value = Engine.GetParameter(target);

        if (knobPickup.Accept(control.Index, controlBank, target, control.Value, value))
        {
            _ = SetParameterCore(target, control.Value);
            knobPickup.Applied(control.Index, Engine.GetParameter(target));
        }
    }

    private void SelectControlBankCore(ControllerControlBank bank)
    {
        if (controlBank != bank)
        {
            Engine.CommitEdit();
            controlBank = bank;

            if (bank == ControllerControlBank.Custom)
            {
                // Custom pots use the performance port. Their physical positions are unknown
                // until a standard bank starts reporting absolute values again.
                knobPickup.Reset();
            }

            else
            {
                knobPickup.Invalidate();
            }
        }
    }

    private ParameterAddress? GetKnobAddress(int knob)
    {
        return controlBank switch
        {
            ControllerControlBank.Device => GetSoundAddress(
                knob,
                soundDeviceLocked ? lockedSoundTrack : Engine.SelectedTrack
            ),
            ControllerControlBank.Volume => knob switch
            {
                < 4 => new(ParameterId.Level, knob),
                4 => new(ParameterId.MasterVolume),
                5 => new(ParameterId.Tempo),
                6 => new(ParameterId.DelayReturn),
                _ => new(ParameterId.ReverbReturn)
            },
            ControllerControlBank.Pan => knob < 4 ? new(ParameterId.Pan, knob) : null,
            ControllerControlBank.SendA => knob < 4 ? new(ParameterId.SendA, knob) : null,
            ControllerControlBank.SendB => knob < 4 ? new(ParameterId.SendB, knob) : null,
            ControllerControlBank.Custom => null,
            _ => null
        };
    }

    private ReadOnlyCollection<ControllerKnobFeedback> CreateKnobFeedback()
    {
        var knobs = new ControllerKnobFeedback[8];

        for (int index = 0; index < knobs.Length; index++)
        {
            if (GetKnobAddress(index) is { } address)
            {
                ParameterDescriptor descriptor = Engine.GetDescriptor(address);
                string trackName = address.Track < 0
                    ? string.Empty
                    : Engine.Tracks[address.Track].Name;
                string name = address.Track < 0
                    || (controlBank == ControllerControlBank.Device
                        && soundDevice == SoundDeviceKind.Instrument)
                    ? descriptor.Name
                    : trackName[..Math.Min(trackName.Length, 8)] + " " + descriptor.Name;
                double target = Engine.GetParameter(address);
                knobs[index] = new(
                    name,
                    knobPickup.Direction(index, target) + descriptor.Format(target)
                );
            }

            else
            {
                knobs[index] = new(string.Empty, string.Empty);
            }
        }

        return Array.AsReadOnly(knobs);
    }
}
