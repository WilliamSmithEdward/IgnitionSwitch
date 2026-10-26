using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class SoundControlTests
{
    public static void PagesAndLifecycle()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        ParameterId[] instrument = [
            ParameterId.Tone, ParameterId.Attack, ParameterId.Release, ParameterId.Drive,
            ParameterId.Pan, ParameterId.SendA, ParameterId.SendB, ParameterId.Level
        ];
        session.SelectTrack(2);
        SoundControlPage page = session.GetSoundControlPage();
        Check.That(
            page.Device == SoundDeviceKind.Instrument
                && page.Name == "Soft atmosphere"
                && page.Parameters
                    .Select(slot => slot.Address)
                    .SequenceEqual(instrument.Select(id => new ParameterAddress(id, 2))),
            "The instrument page must resolve all eight real parameters on the selected track."
        );
        long revision = session.Revision;
        SoundParameterSlot tone = page.Parameters[0];
        Check.That(
            !session.SetParameter(tone.Address, tone.Normalized) && session.Revision == revision,
            "Programmatic refresh and canonical no-op edits must not dirty the project."
        );
        Check.That(
            session.SetParameter(tone.Address, 0.7) && session.Revision == revision + 1,
            "A real parameter edit must advance revision exactly once."
        );
        session.CompleteParameterGesture();
        Check.That(session.Undo(), "A completed parameter gesture must be undoable.");
        Check.Near(
            tone.Normalized,
            session.Engine.GetParameter(tone.Address),
            1e-7,
            "Undo must restore the same parameter target"
        );
        session.ToggleSoundDeviceLock();
        session.SelectTrack(0);
        page = session.GetSoundControlPage();
        Check.That(
            page.Locked && page.Parameters.All(slot => slot.Address.Track == 2),
            "Device Lock must retain the actual instrument across track selection."
        );
        session.ToggleSoundDeviceLock();
        Check.That(
            session
                .GetSoundControlPage()
                .Parameters.All(slot => slot.Address.Track == 0),
            "Unlocking must follow the selected instrument."
        );
        session.SelectSoundDevice(SoundDeviceKind.Delay);
        page = session.GetSoundControlPage();
        Check.That(
            page.Name == "Echo"
                && page.Parameters[0].Address == new ParameterAddress(ParameterId.DelayTime)
                && page.Parameters[3].Address == new ParameterAddress(ParameterId.DelayReturn)
                && page.Parameters
                    .Skip(4)
                    .Select(slot => slot.Address)
                    .SequenceEqual(
                        Enumerable
                            .Range(0, 4)
                            .Select(track => new ParameterAddress(ParameterId.SendA, track))
                    ),
            "Echo must expose four global controls and four real track sends."
        );
        session.SelectSoundDevice(SoundDeviceKind.Reverb);
        page = session.GetSoundControlPage();
        Check.That(
            page.Name == "Room"
                && page.Parameters[0].Address.Id == ParameterId.ReverbDecay
                && page.Parameters[2].Address.Id == ParameterId.ReverbPreDelay
                && page.Parameters
                    .Skip(4)
                    .All(slot => slot.Address.Id == ParameterId.SendB),
            "Room must expose its own controls and reverb sends."
        );
        Check.That(
            page.Parameters.All(slot => slot.Name.Length > 0 && slot.Value.Length > 0),
            "Every advertised control needs a displayed name and physical value."
        );
        session.IsInteractionBlocked = true;
        revision = session.Revision;
        SoundControlPage before = session.GetSoundControlPage();
        Check.That(
            !session.SetParameter(before.Parameters[0].Address, 0.9),
            "Modal work must block parameter writes."
        );
        session.SelectSoundDevice(SoundDeviceKind.Instrument);
        session.ToggleSoundDeviceLock();
        controller.EmitControl(new(MusicControlKind.MoveSoundDevice, -1));
        Check.That(
            session.Revision == revision
                && session.GetSoundControlPage().Device == before.Device
                && session.GetSoundControlPage().Locked == before.Locked,
            "Modal work must block device and lock commands."
        );
        session.IsInteractionBlocked = false;
        session.ToggleSoundDeviceLock();
        using var replacement = new WorkstationEngine();
        session.ApplySession(replacement.CaptureSession());
        page = session.GetSoundControlPage();
        Check.That(
            page.Device == SoundDeviceKind.Instrument && !page.Locked,
            "Opening a project must not retain a stale device lock."
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => session.SelectSoundDevice((SoundDeviceKind)99)
        );
        session.Dispose();
        Check.That(
            !session.SetParameter(new(ParameterId.Tone, 0), 0.8),
            "Queued edits after disposal must be ignored."
        );
    }

    public static void HardwareBanks()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );

        foreach (SoundDeviceKind device in Enum.GetValues<SoundDeviceKind>())
        {
            session.SelectSoundDevice(device);
            SoundControlPage page = session.GetSoundControlPage();

            for (int knob = 0; knob < 8; knob++)
            {
                ParameterAddress address = page.Parameters[knob].Address;
                _ = session.SetParameter(address, 0);
                Turn(controller, knob, 0, ControllerControlBank.Device);
                Turn(controller, knob, 0.75, ControllerControlBank.Device);
                Check.Near(
                    0.75,
                    session.Engine.GetParameter(address),
                    1e-6,
                    $"Device {device} knob {knob} must control its advertised target"
                );
            }
        }

        ParameterAddress[] volume =
        [
            new(ParameterId.Level, 0),
            new(ParameterId.Level, 1),
            new(ParameterId.Level, 2),
            new(ParameterId.Level, 3),
            new(ParameterId.MasterVolume),
            new(ParameterId.Tempo),
            new(ParameterId.DelayReturn),
            new(ParameterId.ReverbReturn)
        ];

        for (int knob = 0; knob < 8; knob++)
        {
            _ = session.SetParameter(volume[knob], 0);
            Turn(controller, knob, 0, ControllerControlBank.Volume);
            Turn(controller, knob, 1, ControllerControlBank.Volume);
            Check.Near(
                1,
                session.Engine.GetParameter(volume[knob]),
                1e-6,
                "Volume bank must expose all eight valid targets"
            );
        }

        foreach ((ControllerControlBank bank, ParameterId id) in new[]
        {
            (ControllerControlBank.Pan, ParameterId.Pan),
            (ControllerControlBank.SendA, ParameterId.SendA),
            (ControllerControlBank.SendB, ParameterId.SendB)
        })
        {
            for (int knob = 0; knob < 4; knob++)
            {
                _ = session.SetParameter(new(id, knob), 0);
                Turn(controller, knob, 0, bank);
                Turn(controller, knob, 0.4, bank);
                Check.Near(
                    0.4,
                    session.Engine.GetParameter(new(id, knob)),
                    1e-6,
                    "Mixer pages must address their own track parameter"
                );
            }

            long revision = session.Revision;
            Turn(controller, 7, 0.2, bank);
            Check.That(
                session.Revision == revision,
                "Missing tracks must not become unrelated parameters."
            );
        }

        session.SelectSoundDevice(SoundDeviceKind.Instrument);
        controller.EmitControl(new(MusicControlKind.MoveSoundDevice, 1));
        Check.That(
            session.GetSoundControlPage().Device == SoundDeviceKind.Delay,
            "Device-down must choose Echo."
        );
        controller.EmitControl(new(MusicControlKind.MoveSoundDevice, -1));
        controller.EmitControl(new(MusicControlKind.ToggleSoundDeviceLock));
        controller.EmitControl(new(MusicControlKind.MoveTrack, 1));
        Check.That(
            session
                .GetSoundControlPage()
                .Parameters.All(slot => slot.Address.Track == 0),
            "Hardware Device Lock must retain its original instrument target."
        );
        session.RefreshFeedback();
        ControllerFeedback feedback = controller.Feedback[^1];
        Check.That(
            feedback.DeviceLocked && feedback.DeviceName == "Velvet keys" && feedback.Knobs?.Count
                == 8,
            "Hardware feedback must describe the actual locked target and all pot slots."
        );
    }

    public static void Pickup()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        var tone = new ParameterAddress(ParameterId.Tone, 0);
        long revision = session.Revision;
        Turn(controller, 0, 0.1, ControllerControlBank.Device);
        Turn(controller, 0, 0.2, ControllerControlBank.Device);
        Check.That(
            session.Revision == revision,
            "An unpicked pot must not move or dirty its parameter."
        );
        Check.Near(
            0.5,
            session.Engine.GetParameter(tone),
            1e-6,
            "A mismatched absolute pot must await its target"
        );
        Turn(controller, 0, 0.7, ControllerControlBank.Device);
        Turn(controller, 0, 0.8, ControllerControlBank.Device);
        Check.Near(
            0.8,
            session.Engine.GetParameter(tone),
            1e-6,
            "A crossing must acquire the target and continue smoothly"
        );
        _ = session.SetParameter(tone, 0.3);
        Turn(controller, 0, 0.9, ControllerControlBank.Device);
        Check.Near(
            0.3,
            session.Engine.GetParameter(tone),
            1e-6,
            "A UI or undo edit must invalidate old pickup"
        );
        Turn(controller, 0, 0.2, ControllerControlBank.Device);
        Check.Near(
            0.2,
            session.Engine.GetParameter(tone),
            1e-6,
            "A subsequent crossing must reacquire the edited target"
        );
        session.SelectTrack(1);
        Turn(controller, 0, 0.1, ControllerControlBank.Device);
        Check.Near(
            0.5,
            session.Engine.GetParameter(new(ParameterId.Tone, 1)),
            1e-6,
            "Track selection must not retain pickup for a different instrument"
        );
        Turn(controller, 0, 0.6, ControllerControlBank.Device);
        Check.Near(
            0.6,
            session.Engine.GetParameter(new(ParameterId.Tone, 1)),
            1e-6,
            "Pickup must follow the actual newly selected address"
        );
        controller.SetConnection(false);
        controller.SetConnection(true);
        Turn(controller, 0, 0.1, ControllerControlBank.Device);
        Check.Near(
            0.6,
            session.Engine.GetParameter(new(ParameterId.Tone, 1)),
            1e-6,
            "A reconnected controller must not reuse stale physical positions"
        );
        controller.EmitControl(
            new(MusicControlKind.SelectControlBank, (int)ControllerControlBank.Custom)
        );
        controller.EmitControl(
            new(MusicControlKind.SelectControlBank, (int)ControllerControlBank.Device)
        );
        Turn(controller, 0, 0.9, ControllerControlBank.Device);
        Check.Near(
            0.6,
            session.Engine.GetParameter(new(ParameterId.Tone, 1)),
            1e-6,
            "A Custom-mode roundtrip must forget positions that could have changed on the performance port"
        );
        Check.That(
            controller.Feedback[^1].Knobs![0].Value.StartsWith(
                "Pickup < ",
                StringComparison.Ordinal
            ),
            "An unmatched pot needs concise directional pickup feedback on the hardware display."
        );
        Turn(controller, 0, 0.6, ControllerControlBank.Device);
        session.IsInteractionBlocked = true;
        Turn(controller, 0, 0.1, ControllerControlBank.Device);
        controller.EmitControl(
            new(MusicControlKind.SelectControlBank, (int)ControllerControlBank.Custom)
        );
        Check.That(
            controller.Feedback[^1].Knobs!.All(knob => knob.Name.Length == 0),
            "Surface bank context must still follow Custom mode during a modal dialog."
        );
        session.IsInteractionBlocked = false;
        Turn(controller, 0, 0.15, ControllerControlBank.Device);
        Check.Near(
            0.6,
            session.Engine.GetParameter(new(ParameterId.Tone, 1)),
            1e-6,
            "Physical motion during a modal picker must not preserve a stale latch after it closes"
        );
        revision = session.Revision;
        Turn(controller, -1, 0.9, ControllerControlBank.Device);
        Turn(controller, 8, 0.9, ControllerControlBank.Device);
        Turn(controller, 0, double.NaN, ControllerControlBank.Device);
        Turn(controller, 0, 2, ControllerControlBank.Device);
        Turn(controller, 0, 0.5, ControllerControlBank.Custom);
        Check.That(
            session.Revision == revision,
            "Invalid and custom inputs must not change the project."
        );
    }

    private static void Turn(
        FakeMusicController controller,
        int index,
        double value,
        ControllerControlBank bank
    )
    {
        controller.EmitControl(new(MusicControlKind.SetKnob, index, value, bank));
    }
}
