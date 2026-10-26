using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class QuantizeCommandTests
{
    public static void Commands()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        SessionSnapshot snapshot = session.Engine.CaptureSession();
        TrackSnapshot[] tracks = [.. snapshot.Tracks];
        tracks[0] = tracks[0] with { Notes = [new LoopNote(60, 100, 0.11, 0.31)] };
        session.ApplySession(snapshot with { Tracks = tracks });
        session.MarkSaved();
        long revision = session.Revision;
        Check.That(
            session.RecordingQuantizationEnabled,
            "Recording should use the grid by default."
        );
        session.RecordingQuantizationEnabled = false;
        Check.That(
            !session.RecordingQuantizationEnabled && !session.HasChanges,
            "A transient recording preference must not dirty saved musical content."
        );
        controller.EmitControl(new(MusicControlKind.QuantizeSelectedTrack));
        Check.Near(
            0,
            session.Engine.CaptureSession().Tracks[0].Notes[0].Beat,
            1e-9,
            "Hardware Quantise must move the onset"
        );
        Check.Near(
            0.31,
            session.Engine.CaptureSession().Tracks[0].Notes[0].Duration,
            1e-9,
            "Quantise must retain expressive duration"
        );
        Check.That(
            session.Revision == revision + 1 && session.HasChanges && session.Undo(),
            "Quantise must be a revision-aware, undoable content edit."
        );
        Check.Near(
            0.11,
            session.Engine.CaptureSession().Tracks[0].Notes[0].Beat,
            1e-9,
            "Undo must restore the original timing"
        );
        session.IsInteractionBlocked = true;
        revision = session.Revision;
        session.RecordingQuantizationEnabled = true;
        controller.EmitControl(new(MusicControlKind.QuantizeSelectedTrack));
        Check.That(
            !session.RecordingQuantizationEnabled
                && !session.QuantizeSelectedTrack()
                && session.Revision == revision,
            "Modal operations must block both quantise entry points and recording preferences."
        );
        Check.Near(
            0.11,
            session.Engine.CaptureSession().Tracks[0].Notes[0].Beat,
            1e-9,
            "Blocked Quantise must preserve timing"
        );
        session.IsInteractionBlocked = false;
        Check.That(
            session.QuantizeSelectedTrack(),
            "The UI entry point must perform the same edit."
        );
        session.MarkSaved();
        revision = session.Revision;
        Check.That(
            !session.QuantizeSelectedTrack() && session.Revision == revision && !session.HasChanges,
            "An already aligned track must not create a phantom edit."
        );
        session.RecordingQuantizationEnabled = true;
        Check.That(
            session.RecordingQuantizationEnabled,
            "Recording preferences must work after the modal closes."
        );
    }
}
