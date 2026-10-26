using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class InputReliabilityTests
{
    public static void RapidChords()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.ToggleRecording();
        long revision = session.Revision;
        Dictionary<(int Note, double Beat), int> expected = [];

        for (int burst = 0; burst < 64; burst++)
        {
            double beat = Math.Round(session.Engine.BeatPosition / 0.25)
                * 0.25
                % session.Engine.LoopBeats;
            int velocity = 64 + burst;

            for (int note = 48; note < 85; note++)
            {
                controller.EmitInput(Note(MidiInputKind.NoteOn, note, velocity));
                expected[(note, beat)] = velocity;
            }

            _ = Check.Render(session.Engine, 0.02);

            for (int note = 48; note < 85; note++)
            {
                controller.EmitInput(Note(MidiInputKind.NoteOff, note));
            }
        }

        Check.That(
            session.Revision == revision + (64 * 37 * 2),
            "Every note-on and final release in the 4,736-event burst must reach the coordinator."
        );
        SessionSnapshot snapshot = session.CaptureForSave().Snapshot;
        Check.That(
            snapshot.Tracks[0].Notes.Count == expected.Count,
            "Every chord pitch/grid position must be recorded, with normal overdub replacement."
        );

        foreach (LoopNote note in snapshot.Tracks[0].Notes)
        {
            Check.That(
                expected.TryGetValue((note.Note, note.Beat), out int velocity)
                    && velocity == note.Velocity,
                "A rapid chord note or its latest retrigger velocity was lost."
            );
        }

        session.Engine.Pause();
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Burst note-off processing must leave no live held voices after their release tails."
        );
    }

    public static void ChannelOwnership()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100, 1));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Releasing one channel must not silence the same pitch still held on another channel."
        );
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60, channel: 1));
        _ = Check.Render(session.Engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "The last channel owner must release the shared track/pitch voice."
        );

        for (int retrigger = 0; retrigger < 64; retrigger++)
        {
            controller.EmitControl(
                new MusicControlEvent(MusicControlKind.SelectTrack, retrigger % 3)
            );
            controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
            Check.That(
                Check.Energy(Check.Render(session.Engine, 0.005)) > 0,
                "A retrigger must remain playable while its target track changes."
            );
        }

        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "A retriggered key must release its latest owner without leaving earlier tracks held."
        );

        controller.EmitInput(Note(MidiInputKind.NoteOn, 64, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 64, 0));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Velocity-zero note-on must release ownership even from another conforming adapter."
        );

        controller.EmitInput(Note(MidiInputKind.NoteOn, 67, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 67, 100, 1));
        controller.EmitInput(new(MidiInputKind.ControlChange, 0, 123, 0, true));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 67, channel: 1));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 67, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 67));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Panic and stale releases must not corrupt the next note's ownership count."
        );
    }

    public static void ProjectReplacement()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        SessionSnapshot replacement = session.Engine.CaptureSession();
        session.IsInteractionBlocked = true;
        controller.EmitInput(new(MidiInputKind.ControlChange, 0, 64, 127, true));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Live playing during the file operation must reach the engine."
        );

        session.ApplySession(replacement);
        session.MarkSaved();
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Applying the replacement project must reset previous live voices."
        );
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 64, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 64));
        _ = Check.Render(session.Engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Replacement must reset coordinator pedal and owner state, so fresh releases cannot stick."
        );
        Check.That(
            session.IsInteractionBlocked && !session.HasChanges,
            "Live audition after replacement must neither close modal isolation nor dirty the loaded project."
        );

        controller.EmitInput(Note(MidiInputKind.NoteOn, 36, 100, 9));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Drum performance must remain playable while a file operation is active."
        );
        controller.EmitInput(new(MidiInputKind.ControlChange, 0, 120, 0, true));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Panic must remain available during modal live performance."
        );
    }

    private static MidiInputEvent Note(MidiInputKind kind, int note, int value = 0, int channel = 0)
    {
        return new(kind, channel, note, value, true);
    }
}
