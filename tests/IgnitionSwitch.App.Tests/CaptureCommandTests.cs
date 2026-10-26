using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class CaptureCommandTests
{
    public static void Commands()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        var results = new List<CaptureResult>();
        session.CaptureCompleted += (_, result) => results.Add(result);

        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            session.SelectTrack(track);
            int channel = track == 3 ? 9 : 0;
            int note = track == 3 ? 37 : 60 + track;
            controller.EmitInput(new(MidiInputKind.NoteOn, channel, note, 110, true));
            _ = Check.Render(session.Engine, 0.15);
            controller.EmitInput(new(MidiInputKind.NoteOff, channel, note, 0, true));
        }

        Check.That(
            !session.HasChanges && session.CanCaptureRecentPerformance,
            "Unrecorded live playing must remain available without dirtying the project."
        );
        long revision = session.Revision;
        controller.EmitControl(new(MusicControlKind.CaptureRecentPerformance));
        Check.That(
            results is [{ Status: CaptureStatus.Captured, NoteCount: 4 }]
                && session.Revision == revision + 1
                && session.HasChanges,
            "Hardware Capture must commit one revision-aware all-track edit and report its result."
        );
        Check.That(
            session.Engine.Tracks.All(track => track.NoteCount == 1),
            "Capture must preserve the original track of every performed part."
        );
        revision = session.Revision;
        controller.EmitControl(new(MusicControlKind.CaptureRecentPerformance));
        Check.That(
            results.Count == 2
                && results[^1].Status == CaptureStatus.NoChanges
                && session.Revision == revision,
            "Repeated Capture without new performance must not create a phantom edit."
        );
        (SessionSnapshot saved, long savedRevision) = session.CaptureForSave();
        session.MarkSaved(savedRevision);
        Check.That(
            !session.HasChanges && saved.Tracks.All(track => track.Notes.Count == 1),
            "Captured notes must participate in the ordinary revision-aware save boundary."
        );
        Check.That(
            session.Undo() && session.Engine.Tracks.All(track => track.NoteCount == 0),
            "One Undo must reverse the complete four-track capture."
        );
        Check.That(
            session.CaptureRecentPerformance()?.Status == CaptureStatus.Captured,
            "The UI Capture command must restore an undone phrase from retained performance."
        );
        session.IsInteractionBlocked = true;
        revision = session.Revision;
        int resultCount = results.Count;
        controller.EmitControl(new(MusicControlKind.CaptureRecentPerformance));
        Check.That(
            !session.CanCaptureRecentPerformance
                && session.CaptureRecentPerformance() is null
                    && session.Revision == revision
                    && results.Count == resultCount,
            "Modal operations must block both capture entry points and result notifications."
        );
        session.IsInteractionBlocked = false;
        TrackSnapshot[] blankTracks = [.. saved.Tracks.Select(track => track with { Notes = [] })];
        session.ApplySession(saved with { Tracks = blankTracks });
        session.MarkSaved();
        Check.That(
            !session.CanCaptureRecentPerformance && session.CaptureRecentPerformance()?.Status
                == CaptureStatus.NoPerformance
                && !session.HasChanges,
            "A replacement project must not capture retrospective data from the old project."
        );
    }

    public static void AudioAvailabilityAndLifecycle()
    {
        var controller = new FakeMusicController();
        var audio = new FakeAudioPlayback();
        using var session = new StudioSession(controller, new WorkstationEngine(), audio);
        session.NoteOn(0, 60, 100);
        _ = Check.Render(session.Engine, 0.2);
        session.NoteOff(0, 60);
        audio.Dispose();
        session.NoteOn(0, 67, 100);
        controller.EmitInput(new(MidiInputKind.NoteOn, 9, 38, 110, true));
        _ = Check.Render(session.Engine, 0.2);
        session.NoteOff(0, 67);
        controller.EmitInput(new(MidiInputKind.NoteOff, 9, 38, 0, true));
        Check.That(
            session.CanCaptureRecentPerformance,
            "An audio outage must not prevent recovery of valid earlier performance."
        );
        CaptureResult? result = session.CaptureRecentPerformance();
        SessionSnapshot snapshot = session.Engine.CaptureSession();
        Check.That(
            result is { Status: CaptureStatus.Captured, NoteCount: 1 }
                && snapshot.Tracks[0].Notes is [{ Note: 60 }]
                && snapshot.Tracks[3].Notes.Count == 0,
            "Unclocked preview and hardware inputs must not pollute retained valid history."
        );
        session.Dispose();
        Check.That(
            !session.CanCaptureRecentPerformance && session.CaptureRecentPerformance() is null,
            "Disposed sessions must ignore capture without accessing their closed engine."
        );

        var unavailableAudio = new FakeAudioPlayback();
        unavailableAudio.Dispose();
        using var unavailable = new StudioSession(
            new FakeMusicController(),
            new WorkstationEngine(),
            unavailableAudio
        );
        unavailable.NoteOn(3, 37, 100);
        unavailable.NoteOff(3, 37);
        _ = Check.Render(unavailable.Engine, 0.1);
        Check.That(
            !unavailable.CanCaptureRecentPerformance
                && unavailable.CaptureRecentPerformance()?.Status
                == CaptureStatus.NoPerformance
                && !unavailable.HasChanges,
            "Starting without audio must not fabricate retrospective timing or saved notes."
        );
    }
}
