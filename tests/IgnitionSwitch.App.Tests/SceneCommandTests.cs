using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class SceneCommandTests
{
    public static void EditingAndHistory()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        RecordClip(session, 0, 0, 60);
        long revision = session.Revision;
        int second = session.CreateScene()
            ?? throw new InvalidOperationException("Scene creation failed.");
        Check.That(
            session.Revision == revision + 1 && session.Engine.Scenes.Count == 2,
            "Creating a scene must change content once and select the new editing row."
        );
        SceneEditorSnapshot editor = session.CaptureSceneEditor();
        Check.That(
            editor.SceneId == second && editor.Notes.All(notes => notes.Count == 0),
            "The editing snapshot must contain only the new row's notes."
        );
        Check.That(
            session.Engine
                .GetClipNotes(new(0, 0))
                .Single().Note
                == 60,
            "Scene creation must not replace the existing clip."
        );
        RecordClip(session, second, 1, 48);
        session.SelectScene(0);
        editor = session.CaptureSceneEditor();
        Check.That(
            editor.Notes[0].Single().Note == 60 && editor.Notes[1].Count == 0,
            "Switching the editor must not combine notes from other scenes."
        );
        session.SelectScene(second);
        editor = session.CaptureSceneEditor();
        Check.That(
            editor.Notes[0].Count == 0 && editor.Notes[1].Single().Note == 48,
            "Each clip must retain its own track and scene identity."
        );
        revision = session.Revision;
        string name = session.Engine.Scenes.Single(scene => scene.Id == second).Name;
        Check.That(
            !session.RenameScene(second, name) && session.Revision == revision,
            "An unchanged scene name must not dirty the project."
        );
        Check.That(
            session.RenameScene(second, "Chorus") && session.Revision == revision + 1,
            "A real rename must advance content revision exactly once."
        );
        Check.That(
            session.Undo()
                && session.Engine.Scenes.Single(scene => scene.Id == second).Name == name,
            "Scene names must use the same chronological undo history as musical edits."
        );
        int duplicate = session.DuplicateScene(second)
            ?? throw new InvalidOperationException("Scene duplication failed.");
        Check.That(
            session.Engine
                .GetClipNotes(new(1, duplicate))
                .Single().Note
                == 48,
            "Duplicating a scene must copy its real clip content."
        );
        revision = session.Revision;
        Check.That(
            session.ClearClip(1, duplicate) && session.Revision == revision + 1,
            "Clearing an exact clip must advance revision once."
        );
        Check.That(
            session.Engine
                .GetClipNotes(new(1, second))
                .Single().Note
                == 48,
            "Clearing a duplicate must preserve the original clip."
        );
        revision = session.Revision;
        Check.That(
            !session.ClearClip(1, duplicate) && session.Revision == revision,
            "Clearing an empty clip must remain a no-op."
        );
        Check.That(
            session.Undo() && session.Engine.GetClipNotes(new(1, duplicate)).Count == 1,
            "Undo must restore the exact cleared clip."
        );
        Check.That(
            session.DeleteScene(duplicate)
                && session.Engine.Scenes.All(scene => scene.Id != duplicate),
            "Delete must remove the selected scene's metadata and clips."
        );
        Check.That(
            session.Undo()
                && session.Engine
                    .GetClipNotes(new(1, duplicate))
                    .Single().Note
                    == 48,
            "Undo must restore a deleted scene with its musical content."
        );
        (SessionSnapshot snapshot, _) = session.CaptureForSave();
        using var restored = new WorkstationEngine();
        restored.ApplySession(snapshot);
        Check.That(
            restored.Scenes.Count == 3
                && restored
                    .GetClipNotes(new(1, duplicate))
                    .Single().Note
                    == 48,
            "Saving must retain scenes beyond the currently editing or playing row."
        );
    }

    public static void LaunchesAndOwnership()
    {
        var controller = new FakeMusicController();
        using var session = new StudioSession(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        RecordClip(session, 0, 0, 60);
        int second = session.CreateScene("Other")
            ?? throw new InvalidOperationException("Scene creation failed.");
        RecordClip(session, second, 0, 64);
        session.SelectScene(0);
        session.MarkSaved();
        long revision = session.Revision;
        session.LaunchScene(0);
        _ = Check.Render(session.Engine, 0.1);
        session.SelectScene(second);
        Check.That(
            session.Engine.GetTrackPlaybackState(0).ActiveSceneId == 0,
            "Browsing an editing scene must leave the audible clip unchanged."
        );
        controller.EmitInput(new(MidiInputKind.NoteOn, 0, 72, 100, true));
        session.LaunchScene(second);
        Check.That(
            session.Engine.GetTrackPlaybackState(0).QueuedSceneId == second,
            "A playing launch must expose its pending target."
        );
        AdvanceBar(session.Engine);
        Check.That(
            session.Engine.GetTrackPlaybackState(0).ActiveSceneId == second,
            "The queued scene must become audible at the musical boundary."
        );
        session.StopClip(0);
        AdvanceBar(session.Engine);
        Check.That(
            session.Engine.GetTrackPlaybackState(0).ActiveSceneId is null
                && session.Engine.IsPlaying
                && Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Lane Stop must preserve global transport and the held live note."
        );
        controller.EmitControl(new(MusicControlKind.MoveTrack, 1));
        controller.EmitInput(new(MidiInputKind.NoteOff, 0, 72, 0, true));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "The original owner must release even after clip and track selection changes."
        );
        Check.That(
            session.Revision == revision && !session.HasChanges,
            "Scene/clip launching, stopping and editing selection are transient, not phantom content edits."
        );
    }

    public static void SurfaceAndModalGuards()
    {
        var controller = new FakeMusicController();
        var audio = new FakeAudioPlayback();
        using var session = new StudioSession(controller, new WorkstationEngine(), audio);
        RecordClip(session, 0, 0, 60);
        int second = session.CreateScene("Next")
            ?? throw new InvalidOperationException("Scene creation failed.");
        RecordClip(session, second, 1, 48);
        session.SelectScene(0);
        session.MarkSaved();
        long revision = session.Revision;
        controller.EmitControl(new(MusicControlKind.TriggerSessionPad, 0, row: 0));
        controller.EmitControl(new(MusicControlKind.TriggerSessionPad, 1, row: 1));
        Check.That(
            session.Engine.EditingSceneId == 0
                && session.Engine.GetTrackPlaybackState(1).QueuedSceneId == second,
            "Lower-row clip launches must not shift the two-row hardware window."
        );
        controller.EmitControl(new(MusicControlKind.CycleSessionRowMode));
        Check.That(
            controller.Feedback[^1].Session?.RowMode == SessionRowMode.Stop,
            "The first context press must select Stop."
        );
        controller.EmitControl(new(MusicControlKind.TriggerSessionPad, 0, row: 1));
        Check.That(
            session.Engine.GetTrackPlaybackState(0).StopQueued && !session.Engine.Tracks[0].IsMuted,
            "The Stop row must queue a real lane stop rather than mutate mute."
        );
        controller.EmitControl(new(MusicControlKind.CycleSessionRowMode));
        controller.EmitControl(new(MusicControlKind.TriggerSessionPad, 1, row: 1));
        Check.That(
            session.Engine.GetTrackSolo(1) && session.Revision == revision,
            "Solo must be real transient monitoring state."
        );
        controller.EmitControl(new(MusicControlKind.CycleSessionRowMode));
        controller.EmitControl(new(MusicControlKind.TriggerSessionPad, 1, row: 1));
        Check.That(
            session.Engine.Tracks[1].IsMuted && session.Revision == revision + 1,
            "Mute must change the actual persistent mixer target."
        );
        controller.EmitControl(new(MusicControlKind.CycleSessionRowMode));
        Check.That(
            controller.Feedback[^1].Session?.RowMode == SessionRowMode.Clips,
            "The fourth context press must restore clip launching."
        );
        controller.EmitControl(new(MusicControlKind.MoveScene, 1));
        controller.EmitControl(new(MusicControlKind.MoveScene, 1));
        Check.That(
            session.Engine.EditingSceneId == second,
            "Scene movement must clamp to existing ordered rows."
        );
        session.ToggleTrackSolo(1);
        _ = session.SetParameter(new(ParameterId.TrackMute, 1), 0);
        session.IsInteractionBlocked = true;
        revision = session.Revision;
        controller.EmitInput(new(MidiInputKind.NoteOn, 0, 67, 100, true));
        controller.EmitControl(new(MusicControlKind.CycleSessionRowMode));
        controller.EmitControl(new(MusicControlKind.MoveScene, -1));
        controller.EmitControl(new(MusicControlKind.LaunchSelectedScene));
        Check.That(
            session.CreateScene() is null
                && session.DuplicateScene(second) is null
                && !session.DeleteScene(second)
                && !session.RenameScene(second, "Blocked")
                && !session.ClearClip(1, second)
                && session.Revision == revision
                && session.Engine.EditingSceneId == second,
            "Modal operations must block scene/clip content and surface mutations."
        );
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) > 1,
            "Live playing must continue through modal scene operations."
        );
        controller.EmitInput(new(MidiInputKind.NoteOff, 0, 67, 0, true));
        session.IsInteractionBlocked = false;
        audio.Dispose();
        session.LaunchScene(second);
        session.LaunchClip(1, second);
        controller.EmitControl(new(MusicControlKind.LaunchSelectedScene));
        Check.That(
            !session.Engine.IsPlaying,
            "Unavailable audio must not start an unclocked scene transport."
        );
        session.SelectScene(0);
        Check.That(
            session.Engine.EditingSceneId == 0,
            "Offline editing remains available without pretending playback can start."
        );
        session.Dispose();
        Check.That(
            session.CreateScene() is null && !session.DeleteScene(0),
            "Late scene actions must be ignored after disposal."
        );
    }

    private static void RecordClip(StudioSession session, int scene, int track, int note)
    {
        session.Stop();
        session.SelectScene(scene);
        session.SelectTrack(track);
        session.ToggleRecording();
        session.NoteOn(track, note, 100);
        _ = Check.Render(session.Engine, 0.1);
        session.NoteOff(track, note);
        session.ToggleRecording();
        session.Stop();
    }

    private static void AdvanceBar(WorkstationEngine engine)
    {
        double remaining = 4 - (engine.BeatPosition % 4);
        _ = Check.Render(engine, (remaining * 60 / engine.Tempo) + 0.002);
    }
}
