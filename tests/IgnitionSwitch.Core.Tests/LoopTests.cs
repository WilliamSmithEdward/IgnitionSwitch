using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class LoopTests
{
    public static Task Transport()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            Check.That(engine.LoopEnabled, "Loop repeat must default to enabled.");
            long revision = engine.NoteRevision;
            engine.LoopEnabled = false;
            Check.That(
                !engine.CanUndo && engine.NoteRevision == revision,
                "Loop preference must not create content history."
            );
            engine.MetronomeEnabled = true;
            engine.Play();
            float[] beforeEnd = new float[((WorkstationEngine.SampleRate * 8) - 1) * 2];
            engine.Render(beforeEnd, 0, beforeEnd.Length);
            Check.That(
                engine.IsPlaying && engine.BeatPosition < 16,
                "Loop-off must render every frame before the pass end."
            );
            engine.Render(new float[2], 0, 2);
            Check.That(
                !engine.IsPlaying && !engine.IsRecording,
                "Loop-off must stop at the exact final frame of the pass."
            );
            Check.Near(16, engine.BeatPosition, 0, "A completed pass must retain its endpoint");
            Check.That(
                Check.Energy(Check.Render(engine, 0.05)) == 0,
                "Natural completion must clear sequenced and metronome tails."
            );
            engine.LoopEnabled = true;
            Check.That(
                !engine.IsPlaying && engine.BeatPosition == 16,
                "Enabling repeat at the endpoint must not restart playback."
            );
            engine.Play();
            Check.Near(
                0,
                engine.BeatPosition,
                0,
                "Play from a completed endpoint must restart at zero"
            );
            Check.That(
                Check.Energy(Check.Render(engine, 0.005)) > 0.1,
                "Restart must reset the click so the first downbeat sounds."
            );
            engine.Stop();
            Check.Near(0, engine.BeatPosition, 0, "Stop must still rewind");
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.Play();
            _ = Check.Render(engine, 16.5);
            engine.LoopEnabled = false;
            double position = engine.BeatPosition;
            engine.Pause();
            engine.LoopEnabled = false;
            _ = Check.Render(engine, 0.25);
            Check.Near(
                position,
                engine.BeatPosition,
                0,
                "Pause must preserve a pending loop-off target and exact position"
            );
            engine.Play();
            _ = Check.Render(engine, 7.5);
            Check.That(
                !engine.IsPlaying && engine.BeatPosition == 16,
                "Disabling repeat after multiple cycles must finish the current pass."
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.Play();
            _ = Check.Render(engine, 8);
            engine.LoopEnabled = false;
            _ = Check.Render(engine, 0.01);
            Check.That(
                engine.IsPlaying,
                "Loop-off at an exact elapsed pass boundary must target the new current pass."
            );
            engine.LoopEnabled = true;
            _ = Check.Render(engine, 8);
            Check.That(engine.IsPlaying, "Re-enabling repeat must cancel a pending natural stop.");
            engine.LoopEnabled = false;
            engine.ApplySession(engine.CaptureSession());
            Check.That(
                !engine.LoopEnabled && engine.CaptureSession().Version == 2,
                "Project application must retain the transient loop preference with canonical schema two."
            );
            engine.Dispose();
            Check.Throws<ObjectDisposedException>(() => engine.LoopEnabled = true);
        }

        return Task.CompletedTask;
    }

    public static Task RecordingAndPartitions()
    {
        VerifyNaturalRecordingEnd();
        VerifyRestartOwnership();
        VerifyAtomicRecordEnd();
        VerifyChordFinalization();
        VerifyPartitions();
        return Task.CompletedTask;
    }

    private static void VerifyNaturalRecordingEnd()
    {
        using WorkstationEngine engine = CreateEngine();
        using WorkstationEngine live = CreateEngine();
        SessionSnapshot source = Check.WithNotes(engine, 0, new LoopNote(60, 100, 15.75, 1));
        TrackSnapshot[] tracks = [.. source.Tracks];
        tracks[3] = tracks[3] with { Notes = new[] { new LoopNote(36, 100, 15.75, 0.5) } };
        engine.ApplySession(source with { Tracks = tracks });
        engine.LoopEnabled = false;
        engine.ToggleRecording();
        _ = Check.Render(engine, 7.9);
        engine.SetSustain(1, true);
        live.SetSustain(1, true);
        engine.NoteOn(1, 48, 100);
        live.NoteOn(1, 48, 100);
        _ = Check.Render(engine, 0.05);
        _ = Check.Render(live, 0.05);
        engine.NoteOff(1, 48);
        live.NoteOff(1, 48);
        _ = Check.Render(engine, 0.05);
        _ = Check.Render(live, 0.05);
        Check.That(
            !engine.IsPlaying && !engine.IsRecording,
            "Natural completion must end an active recording."
        );
        LoopNote recorded = engine
            .CaptureSession()
            .Tracks[1].Notes.Single();
        Check.Near(
            0.25,
            recorded.Duration,
            0,
            "Natural completion must finalize a sustained recorded note at the endpoint"
        );
        Check.That(
            Check
                .Render(engine, 0.03)
                .SequenceEqual(Check.Render(live, 0.03)),
            "Natural completion must remove wrapped sequenced/drum voices while preserving the exact live sustained voice."
        );
        Check.That(
            engine.Undo() && engine.BeatPosition == 16,
            "The finalized recording pass must remain undoable at the retained endpoint."
        );
        Check.That(
            engine.Redo() && engine.BeatPosition == 16,
            "Redo must retain the stopped endpoint."
        );
        engine.SetSustain(1, false);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Pedal release after natural completion must release the original live owner."
        );
        Check.That(
            engine
                .CaptureSession()
                .Tracks[1].Notes.Single()
                == recorded,
            "Later pedal release must not extend the finalized recorded duration."
        );
    }

    private static void VerifyRestartOwnership()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.LoopEnabled = false;
        engine.ToggleRecording();
        engine.NoteOn(3, 36, 100);
        _ = Check.Render(engine, 8);
        Check.That(
            !engine.IsPlaying && engine.BeatPosition == 16,
            "The recorded first pass must complete."
        );
        engine.Play();
        Check.Near(0, engine.BeatPosition, 0, "Completed playback must rewind before restart");
        Check.That(
            Check.Energy(Check.Render(engine, 0.03)) > 0.1,
            "Restart must clear old pending eligibility so recorded downbeats play on the first pass."
        );
        _ = Check.Render(engine, 7.97);
        engine.SetTrackMuted(3, true);
        engine.SetSustain(0, true);
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.03);
        engine.ToggleRecording();
        Check.That(
            engine.IsPlaying && engine.IsRecording && engine.BeatPosition == 0,
            "Record from a completed endpoint must rewind and start a new pass."
        );
        engine.NoteOff(0, 60);
        Check.That(
            Check.Energy(Check.Render(engine, 0.03)) > 0.1,
            "Record restart must preserve an existing live owner and sustain state."
        );
        engine.SetSustain(0, false);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "The retained owner must still accept its final pedal release after restart."
        );
        Check.That(
            engine.Tracks[0].NoteCount == 0,
            "A live note begun before Record restart must not become an invented recording."
        );
    }

    private static void VerifyAtomicRecordEnd()
    {
        using WorkstationEngine engine = CreateEngine();
        Check.That(
            !engine.EndRecording() && !engine.IsPlaying && !engine.CanUndo,
            "Ending inactive recording must not start playback or create history."
        );
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.3);
        double position = engine.BeatPosition;
        Check.That(
            engine.EndRecording() && engine.IsPlaying && !engine.IsRecording,
            "Ending active recording must finalize without pausing playback."
        );
        Check.Near(
            position,
            engine.BeatPosition,
            0,
            "Ending recording must preserve the exact clock position"
        );
        long revision = engine.NoteRevision;
        Check.That(
            !engine.EndRecording() && engine.NoteRevision == revision,
            "Repeated record-end calls must be idempotent."
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.03)) > 0.1,
            "Ending recording must preserve the live held voice."
        );
        engine.NoteOff(0, 60);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "A held owner must still release after atomic record end."
        );
        Check.That(
            engine.Undo() && !engine.CanUndo,
            "Repeated record-end calls must not add history steps."
        );
        engine.Stop();
        engine.LoopEnabled = false;
        engine.ToggleRecording();
        bool previouslyRecording = engine.IsRecording;
        _ = Check.Render(engine, 8);
        Check.That(
            previouslyRecording
                && !engine.EndRecording()
                && !engine.IsPlaying
                && !engine.IsRecording
                && engine.BeatPosition == 16,
            "Record end after natural completion must never restart recording from a stale prior state."
        );
        engine.Dispose();
        Check.Throws<ObjectDisposedException>(() => engine.EndRecording());
    }

    private static void VerifyChordFinalization()
    {
        using WorkstationEngine engine = CreateEngine();
        using WorkstationEngine loaded = CreateEngine();
        engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 80, 15, 0.25)));
        engine.LoopEnabled = false;
        engine.ToggleRecording();
        _ = Check.Render(engine, 7.5);

        for (int track = 0; track < 3; track++)
        {
            foreach (int note in new[] { 60, 64, 67 })
            {
                engine.NoteOn(track, note, 100);
            }
        }

        _ = Check.Render(engine, 0.5);
        SessionSnapshot finalized = engine.CaptureSession();
        Check.That(
            finalized.Tracks
                .Take(3)
                .All(
                    track => track.Notes.Count == 3
                        && track.Notes.All(note => note.Beat == 15 && note.Duration == 1)
                ),
            "Batch finalization must preserve every held chord and replacement duration."
        );
        engine.AllNotesOff();
        loaded.ApplySession(finalized);
        loaded.LoopEnabled = false;
        engine.Play();
        loaded.Play();
        float[] actual = Check.Render(engine, 8.1);
        float[] expected = Check.Render(loaded, 8.1);
        Check.That(
            actual.SequenceEqual(expected),
            "Batch-finalized note schedules must replay identically to canonical project loading."
        );
        engine.ApplySession(finalized);
        engine.ToggleRecording();
        _ = Check.Render(engine, 7.5);
        long revision = engine.NoteRevision;

        for (int track = 0; track < 3; track++)
        {
            foreach (int note in new[] { 60, 64, 67 })
            {
                engine.NoteOn(track, note, 100);
            }
        }

        _ = Check.Render(engine, 0.5);
        Check.That(
            !engine.CanUndo && engine.NoteRevision == revision,
            "An identical multi-note finalization must not rebuild musical content or add history."
        );
    }

    private static void VerifyPartitions()
    {
        using WorkstationEngine whole = CreateEngine();
        using WorkstationEngine split = CreateEngine();
        SessionSnapshot source = Check.WithNotes(
            whole,
            0,
            new LoopNote(60, 100, 0, 0.5),
            new LoopNote(67, 90, 15.75, 1)
        );
        source = source with { Tempo = 137 };
        whole.ApplySession(source);
        split.ApplySession(source);

        foreach (WorkstationEngine engine in new[] { whole, split })
        {
            engine.LoopEnabled = false;
            engine.MetronomeEnabled = true;
            engine.ToggleRecording();
            engine.NoteOn(1, 48, 100);
        }

        int endFrames = (int)Math.Ceiling(16 * 60.0 * WorkstationEngine.SampleRate / 137);
        const int Offset = 3;
        int count = (endFrames + 257) * 2;
        float[] wholeAudio = [.. Enumerable.Repeat(42f, count + (Offset * 2))];
        float[] splitAudio = [.. wholeAudio];
        whole.Render(wholeAudio, Offset, count);
        int[] partitions = [1, 63, 64, 71, 480, 97];
        int written = 0;
        int callback = 0;

        while (written < count)
        {
            int samples = Math.Min(count - written, partitions[callback++ % partitions.Length] * 2);
            split.Render(splitAudio, Offset + written, samples);
            written += samples;
        }

        Check.That(
            wholeAudio.SequenceEqual(splitAudio),
            "Natural completion, live sound, and offset guards must be identical across callback partitions."
        );
        Check.That(
            !whole.IsPlaying
                && !split.IsPlaying
                && whole.BeatPosition == 16
                && split.BeatPosition == 16,
            "Partitioning must retain the exact completed transport state."
        );
        Check.That(
            whole
                .CaptureSession()
                .Tracks[1].Notes.SequenceEqual(split.CaptureSession().Tracks[1].Notes),
            "Partitioning must finalize identical recorded note content."
        );
        Check.Near(
            16,
            whole
                .CaptureSession()
                .Tracks[1].Notes.Single().Duration,
            0,
            "A full-pass held recording must finalize at the exact loop duration"
        );
    }

    private static WorkstationEngine CreateEngine()
    {
        WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }
}
