using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class CaptureAlignmentTests
{
    public static Task FreePhraseAndBoundary()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            AdvanceBeats(engine, 3.25);
            engine.NoteOn(0, 60, 100);
            AdvanceBeats(engine, 0.25);
            engine.NoteOff(0, 60);
            AdvanceBeats(engine, 0.875);
            engine.NoteOn(0, 64, 90);
            AdvanceBeats(engine, 0.375);
            engine.NoteOff(0, 64);

            SessionSnapshot captured = Capture(engine, 2, "A stopped phrase must be capturable.");
            AssertNote(captured, 0, 60, 0, 0.25);
            AssertNote(captured, 0, 64, 1.125, 0.375);
            LoopNote[] previousNotes = [.. captured.Tracks[0].Notes];
            Check.Near(
                0,
                engine.BeatPosition,
                0,
                "Stopped capture must not move project transport"
            );

            AdvanceBeats(engine, 16);
            Check.That(
                engine.HasRecentPerformance
                    && engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
                "Idle capture must retain the last completed phrase without rotating its window."
            );

            engine.NoteOn(1, 72, 110);
            AdvanceBeats(engine, 0.5);
            engine.NoteOff(1, 72);
            captured = Capture(engine, 1, "A note after a long silence must begin a fresh phrase.");
            AssertNote(captured, 1, 72, 0, 0.5);
            Check.That(
                captured.Tracks[0].Notes.SequenceEqual(previousNotes),
                "A new retrospective phrase must merge without clearing earlier project content."
            );
        }

        using (WorkstationEngine engine = CreateEngine())
        {
            engine.NoteOn(0, 60, 100);
            AdvanceBeats(engine, 1);
            engine.NoteOff(0, 60);
            AdvanceBeats(engine, 14.5);
            engine.NoteOn(0, 67, 90);
            AdvanceBeats(engine, 0.5);
            engine.NoteOff(0, 67);

            SessionSnapshot captured = Capture(
                engine,
                2,
                "A phrase ending at exactly sixteen beats must retain its first bar."
            );
            AssertNote(captured, 0, 60, 0, 1);
            AssertNote(captured, 0, 67, 15.5, 0.5);
            Check.That(
                engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
                "Capturing the exact-boundary phrase twice must be idempotent."
            );
        }

        return Task.CompletedTask;
    }

    public static Task ProjectPhaseAndEpoch()
    {
        VerifyProjectPhase();
        VerifyExcludedInput();
        VerifyHeldEpoch(false);
        VerifyHeldEpoch(true);
        return Task.CompletedTask;
    }

    private static void VerifyProjectPhase()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.Play();
        AdvanceBeats(engine, 3.5);
        engine.NoteOn(0, 60, 100);
        AdvanceBeats(engine, 0.25);
        engine.NoteOff(0, 60);
        AdvanceBeats(engine, 10);
        engine.NoteOn(0, 64, 90);
        AdvanceBeats(engine, 0.25);
        engine.NoteOff(0, 64);
        AdvanceBeats(engine, 5);
        engine.NoteOn(0, 67, 110);
        AdvanceBeats(engine, 0.5);
        engine.NoteOff(0, 67);
        double position = engine.BeatPosition;

        SessionSnapshot captured = Capture(
            engine,
            2,
            "A playing capture must use the latest four-bar window."
        );
        Check.That(
            captured.Tracks[0].Notes.All(note => note.Note != 60),
            "A note before the latest complete window must not be captured."
        );
        AssertNote(captured, 0, 64, 13.75, 0.25);
        AssertNote(captured, 0, 67, 3, 0.5);
        Check.That(engine.IsPlaying, "Capture must preserve running playback.");
        Check.Near(position, engine.BeatPosition, 0, "Capture must preserve transport phase");
        AdvanceBeats(engine, 20);
        Check.That(
            engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
            "Playback without new performance must not shift the completed capture window."
        );
    }

    private static void VerifyExcludedInput()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.NoteOn(0, 48, 100, false);
        AdvanceBeats(engine, 0.5);
        engine.NoteOff(0, 48);
        Check.That(
            !engine.HasRecentPerformance
                && engine.CaptureRecentPerformance().Status == CaptureStatus.NoPerformance,
            "Excluded note-on input must not start a retrospective phrase."
        );

        engine.Play();
        AdvanceBeats(engine, 5);
        engine.NoteOn(0, 60, 100);
        AdvanceBeats(engine, 0.5);
        engine.Pause();
        AdvanceBeats(engine, 2);
        engine.NoteOn(1, 67, 100, false);
        AdvanceBeats(engine, 0.5);
        engine.NoteOff(1, 67);
        engine.NoteOff(0, 60);
        SessionSnapshot captured = Capture(
            engine,
            1,
            "Excluded input must not adopt a held gate into a new transport epoch."
        );
        AssertNote(captured, 0, 60, 5, 3);
        Check.That(
            captured.Tracks[1].Notes.Count == 0,
            "Unavailable-audio input must not appear in captured content."
        );
    }

    private static void VerifyHeldEpoch(bool rewind)
    {
        using WorkstationEngine engine = CreateEngine();
        using WorkstationEngine reference = CreateEngine();
        engine.LoopEnabled = !rewind;
        reference.LoopEnabled = !rewind;
        engine.Play();
        reference.Play();
        AdvancePair(engine, reference, rewind ? 15.5 : 5);
        engine.NoteOn(0, 60, 100);
        reference.NoteOn(0, 60, 100);
        AdvancePair(engine, reference, 0.5);

        if (rewind)
        {
            Check.That(
                !engine.IsPlaying && engine.BeatPosition == engine.LoopBeats,
                "The rewind scenario must reach the natural one-pass endpoint."
            );
        }

        else
        {
            engine.Pause();
            reference.Pause();
        }

        AdvancePair(engine, reference, 2);

        if (rewind)
        {
            engine.Play();
            reference.Play();
            Check.Near(0, engine.BeatPosition, 0, "Restart must rewind the project position");
        }

        engine.NoteOn(1, 67, 100);
        reference.NoteOn(1, 67, 100);
        AdvancePair(engine, reference, 0.5);
        double position = engine.BeatPosition;
        SessionSnapshot captured = Capture(
            engine,
            2,
            "A new transport phrase must adopt the currently held gate."
        );
        AssertNote(captured, 0, 60, 0, 0.5);
        AssertNote(captured, 1, 67, 0, 0.5);
        Check.Near(position, engine.BeatPosition, 0, "Held capture must preserve project position");
        float[] audio = Check.Render(engine, 0.025);
        float[] expected = Check.Render(reference, 0.025);
        Check.That(
            Check.Energy(audio) > 1 && audio.SequenceEqual(expected),
            "Adopting held gates must preserve the exact live audio without restarting voices."
        );

        engine.NoteOff(0, 60);
        reference.NoteOff(0, 60);
        engine.NoteOff(1, 67);
        reference.NoteOff(1, 67);
        Check.That(
            Check
                .Render(engine, 0.25)
                .SequenceEqual(Check.Render(reference, 0.25)),
            "Capture must preserve normal release ownership for each adopted held gate."
        );
    }

    private static WorkstationEngine CreateEngine()
    {
        var engine = new WorkstationEngine
        {
            Tempo = 120,
            RecordingQuantizationEnabled = false
        };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static SessionSnapshot Capture(WorkstationEngine engine, int count, string message)
    {
        CaptureResult result = engine.CaptureRecentPerformance();
        Check.That(
            result.Status == CaptureStatus.Captured && result.NoteCount == count,
            $"{message} Got {result.Status} with {result.NoteCount} notes."
        );
        return engine.CaptureSession();
    }

    private static void AssertNote(
        SessionSnapshot session,
        int track,
        int note,
        double beat,
        double duration
    )
    {
        LoopNote captured = session.Tracks[track].Notes.Single(value => value.Note == note);
        Check.Near(
            beat,
            captured.Beat,
            1e-7,
            $"Captured note {note} must retain its expected onset"
        );
        Check.Near(
            duration,
            captured.Duration,
            1e-7,
            $"Captured note {note} must retain its expected duration"
        );
    }

    private static void AdvanceBeats(WorkstationEngine engine, double beats)
    {
        _ = Check.Render(engine, beats / 2);
    }

    private static void AdvancePair(
        WorkstationEngine engine,
        WorkstationEngine reference,
        double beats
    )
    {
        Check.That(
            Check
                .Render(engine, beats / 2)
                .SequenceEqual(Check.Render(reference, beats / 2)),
            "Identical live performance must render identically before retrospective capture."
        );
    }
}
