using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class HistoryTests
{
    public static Task Notes()
    {
        VerifyRecordingParts();
        VerifyHeldRecordings();
        VerifyReplacementAndClear();
        VerifyRestoredOnsets();
        return Task.CompletedTask;
    }

    public static Task MixerAndLifecycle()
    {
        using WorkstationEngine engine = CreateEngine();
        long noteRevision = engine.NoteRevision;
        engine.Tempo = engine.Tempo;
        engine.MasterVolume = engine.MasterVolume;
        engine.SetTrackVolume(0, engine.Tracks[0].Volume);
        engine.SetTrackMuted(0, false);
        engine.ClearSelectedTrack();
        engine.CommitEdit();
        Check.That(
            !engine.CanUndo && !engine.Undo() && !engine.Redo(),
            "No-op edits must not create history."
        );

        for (int value = 1; value <= 100; value++)
        {
            engine.SetTrackVolume(0, 0.8f - (value * 0.006f));
        }

        engine.CommitEdit();
        engine.SetTrackVolume(0, 0.3f);
        engine.CommitEdit();
        Check.That(engine.Undo(), "The second gain gesture must be undoable.");
        Check.Near(
            0.2,
            engine.Tracks[0].Volume,
            1e-6,
            "Undo must restore the previous gesture endpoint"
        );
        Check.That(
            engine.Undo() && !engine.CanUndo,
            "One hundred continuous gain writes must form one history step."
        );
        Check.Near(
            0.8,
            engine.Tracks[0].Volume,
            1e-6,
            "A gain gesture must retain its original value"
        );
        Check.That(
            engine.Redo() && engine.CanRedo,
            "Both gain gestures must be retained for redo."
        );
        float current = engine.Tracks[0].Volume;
        engine.SetTrackVolume(0, 0.6f);
        engine.SetTrackVolume(0, current);
        engine.CommitEdit();
        Check.That(
            engine.CanRedo && engine.Redo(),
            "A net no-op gesture must preserve existing redo history."
        );
        engine.SetTrackVolume(0, 0.4f);
        engine.CommitEdit();
        Check.That(
            !engine.CanRedo && !engine.Redo(),
            "A new content edit must discard obsolete redo."
        );

        SessionSnapshot beforeMixer = engine.CaptureSession();
        engine.Tempo = 145;
        engine.CommitEdit();
        engine.MasterVolume = 0.3f;
        engine.CommitEdit();
        engine.SetTrackVolume(1, 0.25f);
        engine.SetTrackMuted(2, true);

        for (int edit = 0; edit < 4; edit++)
        {
            Check.That(
                engine.Undo(),
                "Each different mixer target must retain its chronological edit."
            );
        }

        VerifySession(beforeMixer, engine.CaptureSession());
        Check.That(
            engine.NoteRevision == noteRevision,
            "Mixer history must not invalidate note geometry."
        );
        engine.ApplySession(engine.CaptureSession());
        Check.That(
            !engine.CanUndo && !engine.CanRedo,
            "Project replacement must establish a fresh history baseline."
        );
        engine.SetTrackMuted(0, true);
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(engine.CaptureSession() with { Tempo = double.NaN })
        );
        Check.That(
            engine.CanUndo && engine.Undo(),
            "A rejected project must preserve valid edit history."
        );
        engine.SelectedTrack = 2;
        engine.MetronomeEnabled = true;
        engine.SetPitchBend(0.5);
        engine.SetSustain(2, true);
        engine.Play();
        engine.Pause();
        engine.Stop();
        Check.That(
            !engine.CanUndo,
            "Performance and transport state must not become persistent content history."
        );
        engine.Dispose();
        Check.Throws<ObjectDisposedException>(() => engine.Undo());
        Check.Throws<ObjectDisposedException>(() => engine.Redo());
        Check.Throws<ObjectDisposedException>(engine.CommitEdit);
        return Task.CompletedTask;
    }

    public static Task Bounds()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            for (int edit = 0; edit < 80; edit++)
            {
                engine.SetTrackMuted(0, edit % 2 == 0);
            }

            engine.MasterVolume = 0.2f;
            engine.MasterVolume = 0.65f;
            engine.CommitEdit();
            Check.That(
                UndoAll(engine) == 64,
                "History must retain at most 64 chronological steps."
            );
            int redone = 0;

            while (engine.Redo())
            {
                redone++;
            }

            Check.That(redone == 64, "Evicted history must not reappear through redo.");
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            LoopNote[] notes =
            [
                .. Enumerable
                    .Range(0, 16)
                    .SelectMany(step => Enumerable
                        .Range(0, 128)
                        .Select(note => new LoopNote(note, 30, step * 0.25, 0.25)))
            ];
            engine.ApplySession(Check.WithNotes(engine, 3, notes));
            float[] quantum = new float[12000];

            for (int pass = 0; pass < 18; pass++)
            {
                engine.ToggleRecording();

                for (int step = 0; step < 16; step++)
                {
                    for (int note = 0; note < 128; note++)
                    {
                        engine.NoteOn(3, note, 70 + pass);
                    }

                    engine.Render(quantum, 0, quantum.Length);
                }

                engine.Stop();
            }

            Check.That(
                UndoAll(engine) == 16,
                "The 32768-delta budget must evict older full-track passes before the step limit."
            );
            Check.That(
                engine.Tracks[3].NoteCount == 2048,
                "History eviction must not discard current musical content."
            );
            Check.That(
                quantum.All(float.IsFinite),
                "Recording at history capacity must retain finite output."
            );
        }

        return Task.CompletedTask;
    }

    private static void VerifyRecordingParts()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.ToggleRecording();

        foreach (int note in new[] { 60, 64, 67 })
        {
            engine.NoteOn(0, note, 100);
        }

        engine.NoteOn(3, 36, 100);
        _ = Check.Render(engine, 0.25);

        foreach (int note in new[] { 60, 64, 67 })
        {
            engine.NoteOff(0, note);
        }

        engine.ToggleRecording();
        SessionSnapshot recorded = engine.CaptureSession();
        long revision = engine.NoteRevision;
        Check.That(engine.CanUndo && engine.Undo(), "A completed recording pass must be undoable.");
        Check.That(
            engine.Tracks.All(track => track.NoteCount == 0),
            "One recording pass must undo all chord and drum notes together."
        );
        Check.That(
            engine.NoteRevision == revision + 1,
            "A note-history operation must advance note revision once."
        );
        Check.That(engine.CanRedo && engine.Redo(), "A recording pass must be restorable.");
        VerifySession(recorded, engine.CaptureSession());

        engine.ApplySession(CreateEngineSnapshot());
        engine.ToggleRecording();
        engine.NoteOn(3, 36, 100);
        engine.SetTrackMuted(0, true);
        engine.NoteOn(3, 38, 100);
        engine.ToggleRecording();
        Check.That(
            engine.Undo() && engine.Tracks[3].NoteCount == 1,
            "A mixer edit must split the following recording part."
        );
        Check.That(
            engine.Undo() && !engine.Tracks[0].IsMuted,
            "The discrete mixer edit must remain between recording parts."
        );
        Check.That(
            engine.Undo() && engine.Tracks[3].NoteCount == 0,
            "The first recording part must remain an independent earlier step."
        );
    }

    private static void VerifyHeldRecordings()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.25);
        double beat = engine.BeatPosition;
        Check.That(engine.Undo(), "Undo must finalize and reverse an active held recording.");
        Check.That(
            engine.IsPlaying && !engine.IsRecording,
            "Undo must end recording while retaining playback state."
        );
        Check.Near(
            beat,
            engine.BeatPosition,
            0,
            "Undo must preserve the exact sample-clock position"
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.025)) > 1,
            "Undo must preserve live held-note ownership and sound."
        );
        engine.NoteOff(0, 60);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Live release after Undo must not strand a voice."
        );
        Check.That(engine.Redo(), "The finalized held duration must remain available to redo.");
        Check.Near(
            0.5,
            engine
                .CaptureSession()
                .Tracks[0].Notes.Single().Duration,
            0,
            "Redo must restore the finalized held duration"
        );
        engine.Stop();
        engine.Play();
        _ = Check.Render(engine, 0.1);
        Check.That(engine.Undo(), "A playing sequenced note must remain undoable.");
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Undo must silence the removed sequenced voice immediately."
        );
    }

    private static void VerifyReplacementAndClear()
    {
        using WorkstationEngine engine = CreateEngine();
        SessionSnapshot original = Check.WithNotes(
            engine,
            0,
            new LoopNote(60, 70, 0, 1),
            new LoopNote(67, 90, 2, 0.5),
            new LoopNote(60, 90, 0, 0.5)
        );
        engine.ApplySession(original);
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 100);
        engine.NoteOff(0, 60);
        engine.Stop();
        SessionSnapshot replaced = engine.CaptureSession();
        Check.That(
            replaced.Tracks[0].Notes.Count == 2,
            "An overdub must merge matching loaded duplicates."
        );
        Check.That(engine.Undo(), "An overdub replacement must be undoable.");
        VerifySession(original, engine.CaptureSession());
        Check.That(engine.Redo(), "An overdub replacement must be redoable.");
        VerifySession(replaced, engine.CaptureSession());
        engine.ClearSelectedTrack();
        Check.That(
            engine.Tracks[0].NoteCount == 0 && engine.Undo(),
            "Clear must be one reversible content edit."
        );
        VerifySession(replaced, engine.CaptureSession());
        Check.That(
            engine.Redo() && engine.Tracks[0].NoteCount == 0,
            "Redo must reapply track clear."
        );

        engine.ApplySession(Check.WithNotes(engine, 3, new LoopNote(36, 100, 0, 0.25)));
        engine.ToggleRecording();
        engine.NoteOn(3, 36, 100);
        engine.Stop();
        Check.That(!engine.CanUndo, "An identical overdub must not create a history step.");
    }

    private static void VerifyRestoredOnsets()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.ApplySession(
            Check.WithNotes(
                engine,
                0,
                new LoopNote(60, 100, 0, 0.5),
                new LoopNote(67, 100, 2, 0.25)
            )
        );
        engine.Play();
        _ = Check.Render(engine, 0.5);
        engine.ClearSelectedTrack();
        Check.That(engine.Undo(), "A cleared track must be restorable during playback.");
        Check.That(
            Check.Energy(Check.Render(engine, 0.2)) == 0,
            "Restoring an empty schedule must not chase note onsets before the current beat."
        );
        _ = Check.Render(engine, 0.3);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 1,
            "A restored future onset must still play at its scheduled beat."
        );
        Check.That(engine.Redo(), "Track clear must remain redoable after a restored onset.");
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Redoing clear must remove the restored sequenced voice immediately."
        );
    }

    private static WorkstationEngine CreateEngine()
    {
        WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static SessionSnapshot CreateEngineSnapshot()
    {
        using WorkstationEngine engine = CreateEngine();
        return engine.CaptureSession();
    }

    private static int UndoAll(WorkstationEngine engine)
    {
        int undone = 0;

        while (engine.Undo())
        {
            undone++;
        }

        return undone;
    }

    private static void VerifySession(SessionSnapshot expected, SessionSnapshot actual)
    {
        Check.That(
            expected.Version == actual.Version
                && expected.Tempo == actual.Tempo
                && expected.LoopBars == actual.LoopBars
                && expected.MasterVolume == actual.MasterVolume,
            "History must preserve project settings."
        );

        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            TrackSnapshot first = expected.Tracks[track];
            TrackSnapshot second = actual.Tracks[track];
            Check.That(
                first.Name == second.Name
                    && first.Instrument == second.Instrument
                    && first.IsMuted == second.IsMuted
                    && first.Volume == second.Volume
                    && first.Notes.SequenceEqual(second.Notes),
                "History must preserve each track's exact content and note ordering."
            );
        }
    }
}
