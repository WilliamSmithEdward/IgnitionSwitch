using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;

internal static class CaptureTests
{
    public static Task MergeAndHistory()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            SessionSnapshot original = Check.WithNotes(
                engine,
                0,
                new LoopNote(60, 30, 0, 1),
                new LoopNote(67, 80, 4, 0.5),
                new LoopNote(60, 40, 0, 0.5)
            );
            engine.ApplySession(original);
            engine.NoteOn(0, 60, 110);
            _ = Check.Render(engine, 0.01);
            engine.NoteOn(0, 64, 90);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(0, 64);
            _ = Check.Render(engine, 0.01);
            engine.NoteOn(0, 60, 80);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(0, 60);
            long revision = engine.NoteRevision;
            CaptureResult result = engine.CaptureRecentPerformance();
            Check.That(
                result == new CaptureResult(CaptureStatus.Captured, 2),
                "Capture must merge unique candidates rather than completion-order duplicates."
            );
            SessionSnapshot captured = engine.CaptureSession();
            LoopNote winner = captured.Tracks[0].Notes.Single(note => note.Note == 60);
            Check.That(
                winner.Velocity == 80 && captured.Tracks[0].Notes.Count == 3,
                "The latest performed onset must replace loaded duplicates while retaining unrelated content."
            );
            Check.Near(
                0.02,
                winner.Duration,
                1e-9,
                "Capture must preserve expressive duration independently of onset snapping"
            );
            Check.That(
                engine.NoteRevision == revision + 1,
                "One capture batch must increment note revision once."
            );
            revision = engine.NoteRevision;
            Check.That(
                engine.CaptureRecentPerformance() == new CaptureResult(CaptureStatus.NoChanges, 2)
                    && engine.NoteRevision == revision,
                "Repeated capture must be a true no-op."
            );
            Check.That(
                engine.Undo() && !engine.CanUndo,
                "Capture must form exactly one undo step."
            );
            VerifyNotes(original, engine.CaptureSession());
            Check.That(
                engine.CaptureRecentPerformance().Status == CaptureStatus.Captured,
                "Undo must not consume the retained performance."
            );
            VerifyNotes(captured, engine.CaptureSession());
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.ToggleRecording();
            engine.NoteOn(3, 37, 100);
            _ = Check.Render(engine, 0.05);
            engine.NoteOff(3, 37);
            engine.NoteOn(0, 60, 100);
            _ = Check.Render(engine, 0.1);
            Check.That(
                engine.CaptureRecentPerformance().Status == CaptureStatus.Captured
                    && engine.IsRecording
                    && engine.IsPlaying,
                "Capture must preserve ordinary recording and active owners."
            );
            double capturedDuration = engine
                .CaptureSession()
                .Tracks[0].Notes.Single().Duration;
            _ = Check.Render(engine, 0.1);
            engine.NoteOff(0, 60);
            _ = engine.EndRecording();
            Check.That(
                engine.Undo(),
                "The later ordinary release must remain its own recording part."
            );
            Check.Near(
                capturedDuration,
                engine
                    .CaptureSession()
                    .Tracks[0].Notes.Single().Duration,
                0,
                "Undo must restore the held snapshot captured before release"
            );
            Check.That(
                engine.Undo() && engine.Tracks[0].NoteCount == 0 && engine.Tracks[3].NoteCount == 1,
                "Capture must remain separate from the earlier completed recording part."
            );
            Check.Near(
                0.25,
                engine
                    .CaptureSession()
                    .Tracks[3].Notes.Single().Duration,
                0,
                "Undo must restore ordinary drum recording's nominal gate"
            );
            Check.That(
                engine.Undo() && engine.Tracks.All(track => track.NoteCount == 0),
                "The earlier ordinary recording must retain chronological undo."
            );
        }

        VerifySustainedWindow();
        return Task.CompletedTask;
    }

    public static async Task BoundsAndLifecycle()
    {
        LoopNote[] full =
        [
            .. Enumerable
                .Range(0, WorkstationEngine.MaximumNotesPerTrack)
                .Select(index => new LoopNote(index % 128, 80, index / 128 * 0.25, 0.25))
        ];

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.ApplySession(Check.WithNotes(engine, 0, full));
            engine.RecordingQuantizationEnabled = false;
            engine.SetTrackVolume(2, 0.4f);
            engine.CommitEdit();
            _ = engine.Undo();
            engine.NoteOn(1, 60, 100);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(1, 60);
            engine.NoteOn(0, 127, 100);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(0, 127);
            SessionSnapshot before = engine.CaptureSession();
            long revision = engine.NoteRevision;
            Check.That(
                engine.CaptureRecentPerformance()
                    == new CaptureResult(CaptureStatus.CapacityExceeded, 0),
                "One overflowing track must reject the entire all-track merge."
            );
            VerifyNotes(before, engine.CaptureSession());
            Check.That(
                engine.CanRedo && engine.NoteRevision == revision && engine.Redo(),
                "Rejected capacity must preserve redo and revision."
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.ApplySession(Check.WithNotes(engine, 0, full));
            engine.NoteOn(0, 127, 100);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(0, 127);
            Check.That(
                engine.CaptureRecentPerformance() == new CaptureResult(CaptureStatus.Captured, 1)
                    && engine.Tracks[0].NoteCount == WorkstationEngine.MaximumNotesPerTrack,
                "A full track must still allow capture replacement of an existing slot."
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            for (int eventIndex = 0; eventIndex < 8193; eventIndex++)
            {
                engine.NoteOn(0, 60, 100);
                engine.NoteOff(0, 60);
            }

            long revision = engine.NoteRevision;
            Check.That(
                engine.CaptureRecentPerformance()
                    == new CaptureResult(CaptureStatus.HistoryTruncated, 0)
                    && !engine.CanUndo
                    && engine.NoteRevision == revision,
                "Relevant ring eviction must reject capture without partial content or history."
            );
            _ = Check.Render(engine, 8.1);
            engine.NoteOn(1, 64, 100);
            _ = Check.Render(engine, 0.01);
            engine.NoteOff(1, 64);
            Check.That(
                engine.CaptureRecentPerformance().Status == CaptureStatus.Captured,
                "A new phrase must recover from old bounded-history loss."
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.NoteOn(1, 48, 100);

            for (int eventIndex = 0; eventIndex < 8193; eventIndex++)
            {
                engine.NoteOn(0, 60, 100);
                engine.NoteOff(0, 60);
            }

            _ = Check.Render(engine, 8.1);
            Check.That(
                engine.CaptureRecentPerformance() == new CaptureResult(CaptureStatus.Captured, 1),
                "Ring loss outside the current held-note window must not falsely reject capture."
            );
        }

        using WorkstationEngine saved = CreateEngine();
        saved.NoteOn(3, 37, 100);
        _ = Check.Render(saved, 0.02);
        saved.NoteOff(3, 37);
        _ = saved.CaptureRecentPerformance();
        string path = Path.Combine(
            Path.GetTempPath(),
            $"IgnitionSwitch-capture-{Guid.NewGuid():N}.ignition.json"
        );

        try
        {
            await SessionStore.SaveAsync(path, saved.CaptureSession());
            SessionSnapshot loaded = await SessionStore.LoadAsync(path);
            saved.ApplySession(loaded);
            Check.That(
                !saved.HasRecentPerformance
                    && saved.CaptureRecentPerformance().Status == CaptureStatus.NoPerformance
                    && loaded.Version == 2,
                "Applying a saved capture must clear transient performance while preserving schema two."
            );
            Check.That(
                saved.Tracks[3].NoteCount == 1,
                "Captured project notes must survive JSON roundtrip."
            );
            saved.Dispose();
            Check.That(!saved.HasRecentPerformance, "Disposal must clear retrospective state.");
            Check.Throws<ObjectDisposedException>(() => saved.CaptureRecentPerformance());
        }

        finally
        {
            File.Delete(path);
        }
    }

    public static Task ClocksAndPartitions()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            engine.RecordingQuantizationEnabled = false;
            engine.NoteOn(0, 60, 100);
            _ = Check.Render(engine, 0.5);
            engine.Tempo = 60;
            _ = Check.Render(engine, 0.5);
            engine.NoteOff(0, 60);
            _ = engine.CaptureRecentPerformance();
            Check.Near(
                1.5,
                engine
                    .CaptureSession()
                    .Tracks[0].Notes.Single().Duration,
                1e-8,
                "Performance timing must integrate tempo actually rendered while project transport is stopped"
            );
            Check.Near(
                0,
                engine.BeatPosition,
                0,
                "Performance clock must not advance stopped project transport"
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 0, 1)));
            engine.Play();
            Check.That(
                Check.Energy(Check.Render(engine, 0.2)) > 0.1,
                "The loop-exclusion fixture must actually render scheduled sound."
            );
            Check.That(
                !engine.HasRecentPerformance
                    && engine.CaptureRecentPerformance().Status == CaptureStatus.NoPerformance,
                "Scheduled loop playback must never record itself into retrospective history."
            );
        }

        using WorkstationEngine whole = CreateEngine();
        using WorkstationEngine split = CreateEngine();

        foreach (WorkstationEngine engine in new[] { whole, split })
        {
            engine.RecordingQuantizationEnabled = false;
            engine.NoteOn(0, 60, 100);
            engine.NoteOn(3, 37, 90);
        }

        RenderPair(whole, split, 4800);

        foreach (WorkstationEngine engine in new[] { whole, split })
        {
            engine.NoteOff(3, 37);
            engine.SetSustain(0, true);
            engine.NoteOff(0, 60);
            engine.Tempo = 90;
        }

        RenderPair(whole, split, 7200);
        whole.SetSustain(0, false);
        split.SetSustain(0, false);
        Check.That(
            whole.CaptureRecentPerformance() == split.CaptureRecentPerformance(),
            "Callback partitions must produce identical capture outcomes."
        );
        VerifyNotes(whole.CaptureSession(), split.CaptureSession());
        whole.NoteOn(1, 67, 100, false);
        whole.NoteOff(1, 67);
        _ = Check.Render(whole, 0.2);
        Check.That(
            whole.CaptureRecentPerformance().Status == CaptureStatus.NoChanges
                && whole.Tracks[1].NoteCount == 0,
            "Unclocked input must not pollute valid prior history before or after rendering resumes."
        );
        return Task.CompletedTask;
    }

    private static void VerifySustainedWindow()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.RecordingQuantizationEnabled = false;
        engine.SetSustain(0, true);
        engine.NoteOn(0, 60, 100);
        engine.NoteOff(0, 60);
        _ = Check.Render(engine, 10);
        engine.NoteOn(1, 64, 90);
        _ = Check.Render(engine, 0.01);
        Check.That(
            engine.CaptureRecentPerformance() == new CaptureResult(CaptureStatus.Captured, 2),
            "Capture must include all sustained/current gates, including notes begun before the retained window."
        );
        LoopNote held = engine
            .CaptureSession()
            .Tracks[0].Notes.Single();
        Check.Near(0, held.Beat, 0, "A free phrase must clip a pre-window held onset to zero");
        Check.Near(16, held.Duration, 1e-8, "A pre-window gate must be bounded to four bars");
        engine.SetSustain(0, false);
        engine.NoteOff(1, 64);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Captured live and sustained owners must still accept their normal releases."
        );
    }

    private static void RenderPair(WorkstationEngine whole, WorkstationEngine split, int frames)
    {
        float[] expected = new float[frames * 2];
        float[] actual = new float[frames * 2];
        whole.Render(expected, 0, expected.Length);
        int written = 0;
        int[] partitions = [1, 63, 64, 97, 256];
        int callback = 0;

        while (written < actual.Length)
        {
            int count = Math.Min(
                actual.Length - written,
                partitions[callback++ % partitions.Length] * 2
            );
            split.Render(actual, written, count);
            written += count;
        }

        Check.That(
            expected.SequenceEqual(actual),
            "The performance clock must not change rendered samples or callback partition equivalence."
        );
    }

    private static WorkstationEngine CreateEngine()
    {
        WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static void VerifyNotes(SessionSnapshot expected, SessionSnapshot actual)
    {
        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            Check.That(
                expected.Tracks[track].Notes.SequenceEqual(actual.Tracks[track].Notes),
                "Capture must preserve exact per-track note content and history ordering."
            );
        }
    }
}
