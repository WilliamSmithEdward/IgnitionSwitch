using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class SequencingTests
{
    public static Task Transport()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        _ = Check.Render(engine, 1);
        Check.Near(0, engine.BeatPosition, 1e-6, "Stopped transport must not advance");
        engine.Play();
        _ = Check.Render(engine, 8.125);
        Check.Near(0.25, engine.BeatPosition, 1e-6, "Four-bar loop must wrap on the audio clock");
        engine.Stop();
        Check.Near(0, engine.BeatPosition, 0, "Stop must return to the beginning");
        Check.That(
            !engine.IsPlaying && !engine.IsRecording,
            "Stop must disable transport and record."
        );
        engine.MetronomeEnabled = true;
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 1,
            "The metronome should sound at the downbeat."
        );
        return Task.CompletedTask;
    }

    public static Task Recording()
    {
        using (WorkstationEngine liveDrums = new())
        using (WorkstationEngine recordedDrums = new())
        {
            liveDrums.NoteOn(3, 36, 100);
            recordedDrums.ToggleRecording();
            recordedDrums.NoteOn(3, 36, 100);
            Check.That(
                Check
                    .Render(liveDrums, 0.1)
                    .SequenceEqual(Check.Render(recordedDrums, 0.1)),
                "Recording a drum on the first downbeat must not double-trigger live playback."
            );
        }

        using WorkstationEngine engine = new() { Tempo = 120 };
        engine.ToggleRecording();
        Check.That(engine.IsPlaying && engine.IsRecording, "Record should start playback.");
        _ = Check.Render(engine, 0.06);
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.29);
        engine.NoteOff(0, 60);
        SessionSnapshot first = engine.CaptureSession();
        LoopNote note = first.Tracks[0].Notes.Single();
        Check.Near(0, note.Beat, 0, "The onset should quantize to a sixteenth note");
        Check.Near(0.5, note.Duration, 0, "The duration should quantize to a sixteenth note");
        engine.Stop();
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 90);
        _ = Check.Render(engine, 0.25);
        engine.NoteOff(0, 60);
        engine.ToggleRecording();
        Check.That(
            engine.Tracks[0].NoteCount == 1,
            "Overdubbing the same note and grid point should replace it."
        );
        engine.Stop();
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.15)) > 5,
            "A recorded loop should play without live input."
        );
        engine.Stop();
        engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 0, 8)));
        engine.ToggleRecording();
        _ = Check.Render(engine, 8);
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.1);
        engine.NoteOff(0, 60);
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.3);
        Check.That(
            Check.Energy(Check.Render(engine, 0.1)) == 0,
            "Replacing a playing loop note must release its superseded voice."
        );
        return Task.CompletedTask;
    }

    public static Task Pause()
    {
        using (WorkstationEngine engine = new()
        {
            Tempo = 120,
            MetronomeEnabled = true
        })
        {
            engine.ApplySession(
                Check.WithNotes(
                    engine,
                    0,
                    new LoopNote(60, 100, 0, 4),
                    new LoopNote(67, 100, 1, 0.25)
                )
            );
            engine.Play();
            _ = Check.Render(engine, 0.5);
            double position = engine.BeatPosition;
            engine.TogglePlayback();
            Check.That(
                !engine.IsPlaying && !engine.IsRecording,
                "Playback toggle must pause the transport."
            );
            Check.That(
                Check.Energy(Check.Render(engine, 0.2)) == 0,
                "Pause must immediately silence sequenced notes and metronome tails."
            );
            Check.Near(
                position,
                engine.BeatPosition,
                0,
                "Rendering while paused must preserve the exact clock position"
            );
            engine.MetronomeEnabled = false;
            engine.TogglePlayback();
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) > 1,
                "A pending onset at the resume boundary must still play."
            );
            Check.Near(
                (position + 0.2) % 16,
                engine.BeatPosition,
                1e-6,
                "Playback must resume from the preserved position"
            );
            _ = Check.Render(engine, 0.3);
            Check.That(
                Check.Energy(Check.Render(engine, 0.05)) == 0,
                "Interrupted loop notes must not be restarted or left sounding on resume."
            );
            engine.Stop();
            Check.Near(0, engine.BeatPosition, 0, "Stop after resume must rewind");
        }

        using (WorkstationEngine engine = new() { Tempo = 120 })
        {
            engine.ToggleRecording();
            engine.NoteOn(0, 60, 100);
            _ = Check.Render(engine, 0.375);
            engine.Pause();
            double position = engine.BeatPosition;
            LoopNote captured = engine
                .CaptureSession()
                .Tracks[0].Notes.Single();
            Check.Near(
                0.75,
                captured.Duration,
                0,
                "Pause must finalize the held recorded duration"
            );
            Check.That(
                !engine.IsRecording && !engine.IsPlaying,
                "Pause must end recording without rewinding."
            );
            engine.Pause();
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) > 1,
                "A live held note must remain playable after recording is paused."
            );
            engine.NoteOff(0, 60);
            _ = Check.Render(engine, 0.2);
            Check.That(
                Check.Energy(Check.Render(engine, 0.05)) == 0,
                "A held live note must still accept note-off while paused."
            );
            Check.Near(
                position,
                engine.BeatPosition,
                0,
                "Repeated pause and live rendering must keep time frozen"
            );
            Check.That(
                engine
                    .CaptureSession()
                    .Tracks[0].Notes.Single()
                    == captured,
                "The later live note-off must not extend or duplicate the paused recording."
            );
            engine.Play();
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) == 0,
                "A finalized note whose onset is before the resume position must not be chased."
            );
            engine.Stop();
        }

        using (WorkstationEngine engine = new() { Tempo = 120 })
        {
            engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 15.75, 0.5)));
            engine.Play();
            _ = Check.Render(engine, 8.05);
            engine.Pause();
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) == 0,
                "Pause must silence a sequenced note sustained across the loop boundary."
            );
            engine.Play();
            Check.That(
                Check.Energy(Check.Render(engine, 0.3)) == 0,
                "Resuming a wrapped note must not strand its previous voice."
            );
            _ = Check.Render(engine, 7.5);
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) > 1,
                "An interrupted wrapped note must retrigger at its next loop onset."
            );
            _ = Check.Render(engine, 0.5);
            Check.That(
                Check.Energy(Check.Render(engine, 0.1)) == 0,
                "A retriggered wrapped note must retain its scheduled release."
            );
        }

        return Task.CompletedTask;
    }

    public static Task WrappingNote()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 15.75, 0.5)));
        engine.Play();
        _ = Check.Render(engine, 7.9);
        Check.That(
            Check.Energy(Check.Render(engine, 0.15)) > 5,
            "A note should sustain across the loop boundary."
        );
        _ = Check.Render(engine, 0.35);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "The wrapped note must release at its scheduled end."
        );
        return Task.CompletedTask;
    }

    public static Task Tracks()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(Check.WithNotes(engine, 3, new LoopNote(36, 100, 0, 0.25)));
        engine.SetTrackMuted(3, true);
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.1)) == 0,
            "A muted track should stay silent."
        );
        engine.Stop();
        engine.SetTrackMuted(3, false);
        engine.SetTrackVolume(3, 0);
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.1)) == 0,
            "Zero track gain must silence playback."
        );
        engine.Stop();
        engine.SelectedTrack = 3;
        engine.ClearSelectedTrack();
        Check.That(engine.Tracks[3].NoteCount == 0, "Clear must remove all selected-track events.");
        return Task.CompletedTask;
    }
}
