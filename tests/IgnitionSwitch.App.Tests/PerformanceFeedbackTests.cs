using IgnitionSwitch.App.Presentation;
using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class PerformanceFeedbackTests
{
    public static void Activity()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        var mailbox = new PadActivityMailbox();
        session.PerformanceChanged += (_, activity) =>
        {
            if (activity.Track == 3)
            {
                mailbox.Record(activity.Note, activity.Velocity, activity.IsPressed);
            }
        };
        session.PerformanceReset += (_, _) => mailbox.Clear();
        session.NoteOn(3, 37, 110);
        session.NoteOff(3, 37);
        PadActivity state = mailbox.Read(37);
        Check.That(
            state is { Sequence: 1, Velocity: 110, IsPressed: false },
            "A tap entirely between UI frames must retain its hit and final release."
        );
        Check.That(
            controller.PadFeedbackEvents.Count == 2
                && controller.PadFeedbackEvents[0] == new PadFeedback(37, 110, true),
            "On-screen notes must request matching hardware feedback once."
        );
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.03)) > 1,
            "Pad feedback must not replace the audio path."
        );

        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 37, 100, true));
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 37, 90, true));
        controller.EmitInput(new(MidiInputKind.NoteOff, 9, 37, 0, true));
        state = mailbox.Read(37);
        Check.That(
            state is { Sequence: 3, Velocity: 90, IsPressed: false },
            "Rapid retriggers must retain each onset sequence and the latest velocity/release."
        );
        controller.DrumPadLayout = new ControllerPadLayout(1, [60]);
        session.NoteOn(3, 60, 100);
        session.Stop();
        Check.That(mailbox.Read(37) == default, "Stop must clear stale visual activity.");
        Check.That(
            controller.PadFeedbackEvents[^1] == new PadFeedback(60, 0, false),
            "Stop must release active pads outside the Launchkey note range."
        );

        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 37, 100, true));
        session.NoteOn(3, 37, 110);
        session.NoteOff(3, 37);
        Check.That(
            mailbox.Read(37).IsPressed,
            "Releasing a preview must retain a physically held pad."
        );
        controller.EmitInput(new(MidiInputKind.NoteOff, 9, 37, 0, true));
        Check.That(
            !mailbox.Read(37).IsPressed,
            "The final physical owner must release pad feedback."
        );
        session.NoteOn(3, 37, 100);
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 37, 110, true));
        controller.EmitInput(new(MidiInputKind.NoteOff, 9, 37, 0, true));
        Check.That(
            mailbox.Read(37).IsPressed,
            "Releasing hardware must retain a held preview owner."
        );
        session.NoteOff(3, 37);
        Check.That(
            !mailbox.Read(37).IsPressed,
            "The final preview owner must release pad feedback."
        );

        var concurrent = new PadActivityMailbox();
        _ = Parallel.For(0, 3000, iteration =>
        {
            concurrent.Record(38, (iteration % 127) + 1, true);
            concurrent.Record(38, 0, false);
        });
        concurrent.Record(38, 0, false);
        state = concurrent.Read(38);
        Check.That(
            state.Sequence == 3000 && !state.IsPressed,
            "Concurrent producers must not lose hit sequences or the final release."
        );
    }

    public static void HistoryCommands()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.Engine.SetTrackVolume(0, 0.4f);
        session.Engine.CommitEdit();
        session.MarkChanged();
        long revision = session.Revision;
        controller.EmitControl(new(MusicControlKind.Undo));
        Check.Near(
            0.8,
            session.Engine.Tracks[0].Volume,
            1e-6,
            "Hardware Undo must restore a mixer edit"
        );
        Check.That(
            session.Revision == revision + 1 && session.Engine.CanRedo,
            "A successful undo must advance saved-content revision."
        );
        controller.EmitControl(new(MusicControlKind.Redo));
        Check.Near(
            0.4,
            session.Engine.Tracks[0].Volume,
            1e-6,
            "Hardware Redo must restore the edit"
        );
        session.IsInteractionBlocked = true;
        revision = session.Revision;
        controller.EmitControl(new(MusicControlKind.Undo));
        Check.That(
            session.Revision == revision && !session.Undo(),
            "Modal operations must block project undo while retaining performance."
        );
        session.IsInteractionBlocked = false;
        Check.That(
            session.Undo() && session.Redo(),
            "UI and hardware history entry points must share engine behavior."
        );
    }
}
