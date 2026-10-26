using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class InputDiagnosticsTests
{
    public static void CountingAndReset()
    {
        var controller = new FakeMusicController();
        var audio = new FakeAudioPlayback();
        using var session = new StudioSession(controller, new WorkstationEngine(), audio);
        Check.That(
            session.ReadInputDiagnostics() == default,
            "Input diagnostics must begin empty."
        );
        controller.EmitInput(new(MidiInputKind.NoteOn, 0, 60, 100, true));
        controller.EmitInput(new(MidiInputKind.NoteOff, 0, 60, 0, true));
        session.NoteOn(3, 36, 100);
        session.NoteOff(3, 36);
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 36, 100, false));
        Check.That(
            session.ReadInputDiagnostics().DrumNoteOnCount == 0,
            "Keyboard, preview, and unrouted control events are not received drum performance."
        );

        for (int index = 0; index < 1024; index++)
        {
            int note = 36 + (index % 16);
            int velocity = (index % 127) + 1;
            controller.EmitInput(new(MidiInputKind.NoteOn, 9, note, velocity, true));
            controller.EmitInput(new(MidiInputKind.NoteOn, 9, note, 0, true));
            controller.EmitInput(new(MidiInputKind.NoteOff, 9, note, 64, true));
        }

        PerformanceInputDiagnostics.Snapshot snapshot = session.ReadInputDiagnostics();
        Check.That(
            snapshot.DrumNoteOnCount == 1024
                && snapshot.LastDrumVelocity == 8
                && snapshot.MinimumDrumVelocity == 1
                && snapshot.MaximumDrumVelocity == 127,
            "Rapid note-ons must retain the count and exact velocity statistics without counting releases."
        );
        session.IsInteractionBlocked = true;
        audio.Dispose();
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 37, 15, true));
        controller.EmitInput(new(MidiInputKind.NoteOff, 9, 37, 0, true));
        Check.That(
            session.ReadInputDiagnostics().DrumNoteOnCount == 1025
                && session.ReadInputDiagnostics().LastDrumVelocity == 15,
            "Received drum input must count during modal operations and unavailable audio."
        );
        session.IsInteractionBlocked = false;
        session.Stop();
        controller.EmitInput(new(MidiInputKind.NoteOn, 0, 60, 100, true));
        session.Engine.SetTrackVolume(0, 0.6f);
        session.Engine.CommitEdit();
        session.MarkChanged();
        long revision = session.Revision;
        bool canUndo = session.Engine.CanUndo;
        bool canRedo = session.Engine.CanRedo;
        session.ResetInputDiagnostics();
        Check.That(
            session.ReadInputDiagnostics() == default,
            "Reset must clear the count and all velocity statistics."
        );
        Check.That(
            session.Revision == revision
                && session.HasChanges
                && session.Engine.CanUndo == canUndo
                && session.Engine.CanRedo == canRedo,
            "Resetting diagnostics must not edit content, revision, or history."
        );
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Resetting diagnostics must preserve a held live note."
        );
        controller.EmitInput(new(MidiInputKind.NoteOff, 0, 60, 0, true));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Resetting diagnostics must preserve the matching release owner."
        );
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 36, 64, true));
        snapshot = session.ReadInputDiagnostics();
        Check.That(
            snapshot == new PerformanceInputDiagnostics.Snapshot(1, 64, 64, 64),
            "The first input after reset must establish a fresh velocity range."
        );
        VerifyAllocationFreeCounter();
    }

    private static void VerifyAllocationFreeCounter()
    {
        var diagnostics = new PerformanceInputDiagnostics();
        diagnostics.RecordDrumNoteOn(0);
        diagnostics.RecordDrumNoteOn(-1);
        diagnostics.RecordDrumNoteOn(128);
        Check.That(
            diagnostics.Read() == default,
            "Invalid velocities and velocity-zero releases must not count."
        );

        for (int index = 0; index < 1000; index++)
        {
            diagnostics.RecordDrumNoteOn(64);
        }

        diagnostics.Reset();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 100000; index++)
        {
            diagnostics.RecordDrumNoteOn(64);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check.That(
            allocated == 0 && diagnostics.Read().DrumNoteOnCount == 100000,
            "Counting must be bounded storage with no per-hit allocation."
        );
    }
}
