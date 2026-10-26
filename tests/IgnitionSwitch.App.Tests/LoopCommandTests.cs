using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class LoopCommandTests
{
    public static void Commands()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.Engine.Tempo = 180;
        session.MarkSaved();
        Check.That(session.LoopEnabled, "Repeat should be enabled by default.");
        controller.EmitControl(new(MusicControlKind.ToggleLoop));
        Check.That(
            !session.LoopEnabled && !session.HasChanges && !controller.Feedback[^1].LoopEnabled,
            "The hardware Loop command must update a transient preference and hardware feedback."
        );
        session.IsInteractionBlocked = true;
        session.LoopEnabled = true;
        session.ToggleLoop();
        controller.EmitControl(new(MusicControlKind.ToggleLoop));
        Check.That(!session.LoopEnabled, "Modal operations must block all Loop entry points.");
        session.IsInteractionBlocked = false;

        session.ToggleRecording();
        controller.EmitInput(new(MidiInputKind.NoteOn, 0, 60, 110, true));
        _ = Check.Render(session.Engine, (session.Engine.LoopBeats / 3) + 0.05);
        Check.That(
            !session.Engine.IsPlaying && !session.Engine.IsRecording && session.HasChanges,
            "A single recorded pass must finish and retain unsaved musical content."
        );
        Check.Near(
            session.Engine.LoopBeats,
            session.Engine.BeatPosition,
            1e-9,
            "The completed pass must retain its end position"
        );
        session.RefreshFeedback();
        Check.That(
            !controller.Feedback[^1].Playing && !controller.Feedback[^1].Recording,
            "Hardware feedback must reflect natural completion."
        );
        (SessionSnapshot snapshot, long revision) = session.CaptureForSave();
        Check.That(
            snapshot.Tracks[0].Notes is [{ Note: 60, Duration: > 0 and <= 16 }],
            "Saving after natural completion must retain the finalized held recording."
        );
        session.MarkSaved(revision);
        Check.That(
            !session.HasChanges,
            "The finalized recording must save at a consistent revision."
        );
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 0,
            "Single-pass completion must preserve a physically held live note."
        );
        controller.EmitInput(new(MidiInputKind.NoteOff, 0, 60, 0, true));
        _ = Check.Render(session.Engine, 0.5);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "A retained hardware owner must still release normally after completion."
        );
        session.ToggleLoop();
        Check.That(
            session.LoopEnabled && !session.Engine.IsPlaying,
            "Enabling repeat must not autoplay."
        );
        session.TogglePlayback();
        Check.Near(
            0,
            session.Engine.BeatPosition,
            1e-9,
            "Play after completion must restart at the beginning"
        );
        Check.That(session.Engine.IsPlaying, "The fresh pass must start normally.");
        session.Stop();
        Check.Near(0, session.Engine.BeatPosition, 1e-9, "Stop must still rewind.");
    }
}
