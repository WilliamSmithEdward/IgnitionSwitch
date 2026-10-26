using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;

namespace IgnitionSwitch.App;

internal static class StudioSessionTests
{
    public static void Ownership()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.ToggleRecording();
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        _ = Check.Render(session.Engine, 0.1);
        controller.EmitControl(new MusicControlEvent(MusicControlKind.SelectTrack, 2));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        _ = Check.Render(session.Engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "A keyboard note must release its original instrument after a track change."
        );
        Check.That(
            session.Engine.Tracks[0].NoteCount == 1 && session.Engine.Tracks[2].NoteCount == 0,
            "A note duration must be recorded on its original track."
        );
        controller.EmitInput(Note(MidiInputKind.NoteOn, 36, 100, channel: 9));
        Check.That(
            session.Engine.Tracks[3].NoteCount == 1,
            "General MIDI drum performance must target the drum track independently of selection."
        );
        controller.EmitInput(new MidiInputEvent(MidiInputKind.NoteOn, 0, 96, 127, false));
        Check.That(
            session.Engine.SelectedTrack == 2,
            "Raw surface events must be interpreted only by the injected hardware adapter."
        );
    }

    public static void Sustain()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        controller.EmitInput(Control(64, 127));
        controller.EmitControl(new MusicControlEvent(MusicControlKind.SelectTrack, 1));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 45, 100));
        _ = Check.Render(session.Engine, 0.1);
        controller.EmitInput(Note(MidiInputKind.NoteOff, 45));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.2)) > 1,
            "A track selected while the pedal is already down must sustain new notes."
        );
        controller.EmitControl(new MusicControlEvent(MusicControlKind.SelectTrack, 2));
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        _ = Check.Render(session.Engine, 0.1);
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        controller.EmitInput(Control(64, 0));
        _ = Check.Render(session.Engine, 0.8);
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "Pedal-up must release sustained notes on every previous track."
        );
    }

    public static void ModalIsolation()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.ToggleRecording();
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        _ = Check.Render(session.Engine, 0.2);
        session.IsInteractionBlocked = true;
        Check.That(
            !session.Engine.IsRecording
                && !session.Engine.IsPlaying
                && session.Engine.Tracks[0].NoteCount == 1,
            "Beginning a modal operation must stop playback and finalize held recorded notes."
        );
        long revision = session.Revision;
        controller.EmitInput(Note(MidiInputKind.NoteOn, 61, 100));
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        controller.EmitInput(Control(64, 0));
        controller.EmitControl(new MusicControlEvent(MusicControlKind.SetTempo, value: 150));
        controller.EmitControl(new MusicControlEvent(MusicControlKind.ToggleTrackMute, 0));
        controller.EmitControl(new MusicControlEvent(MusicControlKind.ToggleRecording));
        Check.That(
            session.Revision == revision
                && session.Engine.Tempo == 100
                && !session.Engine.Tracks[0].IsMuted,
            "Hardware must not edit the project behind a modal dialog."
        );
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.05)) == 0,
            "New notes must remain silent while the modal transaction is active."
        );
        session.IsInteractionBlocked = false;
        controller.EmitInput(Note(MidiInputKind.NoteOn, 62, 100));
        Check.That(
            Check.Energy(Check.Render(session.Engine, 0.1)) > 1,
            "Performance must resume after the modal transaction ends."
        );
    }

    public static void GenericPlaybackPauseResume()
    {
        FakeMusicController uiController = new();
        FakeMusicController hardwareController = new();
        using StudioSession uiSession = new(
            uiController,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        using StudioSession hardwareSession = new(
            hardwareController,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        uiSession.Engine.Tempo = 120;
        hardwareSession.Engine.Tempo = 120;
        LoopNote loop = new(60, 100, 0, 4);
        uiSession.Engine.ApplySession(Check.WithNotes(uiSession.Engine, 0, loop));
        hardwareSession.Engine.ApplySession(Check.WithNotes(hardwareSession.Engine, 0, loop));
        LaunchkeyControlMapper mapper = new();
        Check.That(
            mapper.TryMap(
                new MidiInputEvent(
                    MidiInputKind.ControlChange,
                    LaunchkeyMk3Protocol.ControlChannel,
                    LaunchkeyMk3Protocol.Play,
                    127,
                    false
                ),
                out MusicControlEvent? hardwarePlay
            ),
            "The mapped hardware Play press must produce a transport command."
        );

        uiSession.TogglePlayback();
        hardwareController.EmitControl(hardwarePlay!);
        Check.That(
            Check
                .Render(uiSession.Engine, 0.375)
                .SequenceEqual(Check.Render(hardwareSession.Engine, 0.375)),
            "UI and hardware commands must start the same musical playback."
        );
        double position = uiSession.Engine.BeatPosition;
        uiSession.TogglePlayback();
        hardwareController.EmitControl(hardwarePlay!);
        Check.That(
            !uiSession.Engine.IsPlaying && !hardwareSession.Engine.IsPlaying,
            "Both transport entry points must pause running playback."
        );
        Check.Near(
            position,
            hardwareSession.Engine.BeatPosition,
            0,
            "Both transport entry points must preserve the same position"
        );
        Check.That(
            Check.Energy(Check.Render(uiSession.Engine, 0.1)) == 0
                && Check.Energy(Check.Render(hardwareSession.Engine, 0.1)) == 0,
            "Paused sequenced voices must remain silent through either command path."
        );
        Check.Near(
            position,
            uiSession.Engine.BeatPosition,
            0,
            "The UI pause must freeze the clock"
        );
        Check.Near(
            position,
            hardwareSession.Engine.BeatPosition,
            0,
            "The hardware pause must freeze the clock"
        );
        uiSession.TogglePlayback();
        hardwareController.EmitControl(new MusicControlEvent(MusicControlKind.TogglePlayback));
        _ = Check.Render(uiSession.Engine, 0.125);
        _ = Check.Render(hardwareSession.Engine, 0.125);
        Check.Near(
            position + 0.25,
            uiSession.Engine.BeatPosition,
            1e-6,
            "UI resume must advance from its paused position"
        );
        Check.Near(
            uiSession.Engine.BeatPosition,
            hardwareSession.Engine.BeatPosition,
            0,
            "The generic transport command must resume identically to the UI action"
        );
        uiSession.Stop();
        hardwareController.EmitControl(new MusicControlEvent(MusicControlKind.Stop));
        Check.Near(0, uiSession.Engine.BeatPosition, 0, "UI Stop must rewind");
        Check.Near(0, hardwareSession.Engine.BeatPosition, 0, "Hardware Stop must rewind");

        uiSession.Engine.ClearSelectedTrack();
        uiSession.ToggleRecording();
        uiController.EmitInput(Control(64, 127));
        uiController.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        _ = Check.Render(uiSession.Engine, 0.375);
        long recordingRevision = uiSession.Revision;
        uiController.EmitControl(hardwarePlay!);
        Check.That(
            uiSession.Revision > recordingRevision && !uiSession.Engine.IsRecording,
            "Pausing recording through the hardware command must mark finalized note data changed."
        );
        (SessionSnapshot snapshot, long capturedRevision) = uiSession.CaptureForSave();
        LoopNote capturedNote = snapshot.Tracks[0].Notes.Single();
        Check.Near(
            0.75,
            capturedNote.Duration,
            0,
            "The saved snapshot must contain the duration finalized by pause"
        );
        uiSession.MarkSaved(capturedRevision);
        Check.That(
            !uiSession.HasChanges,
            "The finalized paused revision must be independently saveable."
        );
        uiController.EmitControl(new MusicControlEvent(MusicControlKind.SelectTrack, 1));
        uiController.EmitInput(Note(MidiInputKind.NoteOff, 60));
        uiController.EmitInput(Note(MidiInputKind.NoteOn, 45, 100));
        uiController.EmitInput(Note(MidiInputKind.NoteOff, 45));
        Check.That(
            Check.Energy(Check.Render(uiSession.Engine, 0.1)) > 1,
            "Pause must preserve the down pedal for original and newly selected live note owners."
        );
        uiController.EmitInput(Control(64, 0));
        _ = Check.Render(uiSession.Engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(uiSession.Engine, 0.05)) == 0,
            "Note-off and pedal-up after pause must release all original owners without stuck voices."
        );
        Check.That(
            uiSession
                .CaptureForSave()
                .Snapshot.Tracks[0].Notes.Single()
                == capturedNote,
            "Live releases after pause must not extend the finalized recording."
        );
    }

    public static void Revisions()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.MarkChanged();
        (SessionSnapshot snapshot, long savedRevision) = session.CaptureForSave();
        controller.EmitControl(new MusicControlEvent(MusicControlKind.SetTempo, value: 130));
        session.MarkSaved(savedRevision);
        Check.That(
            session.HasChanges && snapshot.Tempo == 100 && session.Engine.Tempo == 130,
            "Saving an older captured snapshot must not erase later edits from the dirty state."
        );
        session.MarkSaved();
        Check.That(
            !session.HasChanges,
            "Marking the current revision saved must clear its dirty state."
        );
        session.ToggleRecording();
        controller.EmitInput(Note(MidiInputKind.NoteOn, 60, 100));
        long pendingRevision = session.Revision;
        _ = Check.Render(session.Engine, 0.2);
        controller.EmitInput(Note(MidiInputKind.NoteOff, 60));
        Check.That(
            session.Revision > pendingRevision && session.Engine.Tracks[0].NoteCount == 1,
            "Finalizing a note duration must advance the content revision used by the timeline."
        );
    }

    public static void Feedback()
    {
        FakeMusicController controller = new();
        using StudioSession session = new(
            controller,
            new WorkstationEngine(),
            new FakeAudioPlayback()
        );
        session.RefreshFeedback();
        session.RefreshFeedback();
        Check.That(
            controller.Feedback.Count == 1,
            "Unchanged hardware state must avoid duplicate writes."
        );
        session.Engine.SetTrackMuted(0, true);
        session.RefreshFeedback();
        Check.That(
            controller.Feedback.Count == 2 && controller.Feedback[^1].TrackMuted[0],
            "A mute change must invalidate the hardware feedback cache."
        );
        SessionSnapshot snapshot = session.Engine.CaptureSession();
        TrackSnapshot[] tracks = [.. snapshot.Tracks];
        tracks[0] = tracks[0] with { Name = "Renamed instrument" };
        session.Engine.ApplySession(snapshot with { Tracks = tracks });
        session.RefreshFeedback();
        Check.That(
            controller.Feedback.Count == 3
                && controller.Feedback[^1].TrackNames[0] == "Renamed instrument",
            "A loaded track name must invalidate the hardware feedback cache."
        );
        controller.SetConnection(true);
        session.RefreshFeedback();
        Check.That(
            controller.Feedback.Count == 4,
            "A new connection must repaint identical application state."
        );
    }

    public static void Shutdown()
    {
        FakeMusicController controller = new();
        FakeAudioPlayback audio = new();
        StudioSession session = new(controller, new WorkstationEngine(), audio);
        Action<MidiInputEvent> queuedInput = controller.CaptureInputCallback();
        Action<MusicControlEvent> queuedControl = controller.CaptureControlCallback();
        Action queuedStatus = controller.CaptureStatusCallback();
        session.Dispose();
        queuedInput(Note(MidiInputKind.NoteOn, 60, 100));
        queuedInput(Note(MidiInputKind.NoteOff, 60));
        queuedControl(new MusicControlEvent(MusicControlKind.ToggleTrackMute, 0));
        queuedStatus();
        session.RefreshFeedback();
        session.Dispose();
        Check.That(
            controller.IsDisposed && audio.IsDisposed,
            "Shutdown must dispose both injected adapters."
        );
        Check.Throws<ObjectDisposedException>(session.Engine.Play);
    }

    private static MidiInputEvent Note(MidiInputKind kind, int note, int value = 0, int channel = 0)
    {
        return new MidiInputEvent(kind, channel, note, value, true);
    }

    private static MidiInputEvent Control(int number, int value)
    {
        return new MidiInputEvent(MidiInputKind.ControlChange, 0, number, value, true);
    }
}
