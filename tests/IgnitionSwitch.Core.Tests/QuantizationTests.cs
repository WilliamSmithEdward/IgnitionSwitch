using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class QuantizationTests
{
    public static Task RecordingPolicy()
    {
        using (WorkstationEngine engine = CreateEngine())
        {
            Check.That(
                engine.RecordingQuantizationEnabled,
                "Recording quantization must default to enabled."
            );
            long revision = engine.NoteRevision;
            engine.RecordingQuantizationEnabled = false;
            Check.That(
                !engine.CanUndo && engine.NoteRevision == revision,
                "The transient recording preference must not create content history."
            );
            SessionSnapshot snapshot = engine.CaptureSession();
            engine.ApplySession(snapshot);
            Check.That(
                !engine.RecordingQuantizationEnabled,
                "Applying a project must not replace the current transient recording preference."
            );
            engine.ToggleRecording();
            _ = Check.Render(engine, 0.07);
            double onset = engine.BeatPosition;
            engine.NoteOn(0, 60, 100);
            _ = Check.Render(engine, 0.11);
            double duration = engine.BeatPosition - onset;
            engine.NoteOff(0, 60);
            engine.Stop();
            LoopNote raw = engine
                .CaptureSession()
                .Tracks[0].Notes.Single();
            Check.Near(
                onset,
                raw.Beat,
                0,
                "Disabled quantization must preserve audio-clock onset timing"
            );
            Check.Near(
                duration,
                raw.Duration,
                0,
                "Disabled quantization must preserve expressive duration"
            );
            using WorkstationEngine restored = CreateEngine();
            restored.ApplySession(engine.CaptureSession());
            Check.That(
                restored.RecordingQuantizationEnabled && restored.CaptureSession().Version == 3,
                "Projects retain schema three and do not persist the recording preference."
            );
        }

        VerifyPerNotePolicy();
        VerifyShortRawTap();
        VerifyRawBounds();
        return Task.CompletedTask;
    }

    public static Task SelectedTrack()
    {
        VerifyCollisionsAndHistory();
        VerifyHeldOwners();
        VerifyPendingLoopEligibility();
        VerifyCurrentPosition();
        return Task.CompletedTask;
    }

    private static void VerifyPerNotePolicy()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.06);
        engine.NoteOn(0, 60, 100);
        engine.RecordingQuantizationEnabled = false;
        _ = Check.Render(engine, 0.29);
        engine.NoteOff(0, 60);
        double rawOnset = engine.BeatPosition;
        engine.NoteOn(0, 64, 100);
        engine.RecordingQuantizationEnabled = true;
        _ = Check.Render(engine, 0.11);
        double rawDuration = engine.BeatPosition - rawOnset;
        engine.NoteOff(0, 64);
        engine.Stop();
        IReadOnlyList<LoopNote> notes = engine.CaptureSession().Tracks[0].Notes;
        Check.Near(
            0,
            notes[0].Beat,
            0,
            "A held note must retain the enabled policy active at its onset"
        );
        Check.Near(
            0.5,
            notes[0].Duration,
            0,
            "Changing policy must not change a held note's duration policy"
        );
        Check.Near(
            rawOnset,
            notes[1].Beat,
            0,
            "A held raw note must retain its original onset policy"
        );
        Check.Near(
            rawDuration,
            notes[1].Duration,
            0,
            "Enabling quantization must not alter an already-held raw duration"
        );
    }

    private static void VerifyShortRawTap()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.RecordingQuantizationEnabled = false;
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 100);
        engine.NoteOff(0, 60);
        engine.Stop();
        LoopNote note = engine
            .CaptureSession()
            .Tracks[0].Notes.Single();
        Check.That(
            note.Duration is > 0 and < 0.001,
            "A raw tap between callbacks needs a positive minimum duration without a sixteenth-note gate."
        );
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 0.1,
            "A recorded sub-callback raw tap must remain audible through the smooth minimum onset."
        );
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "A minimum-duration raw tap must eventually release."
        );
    }

    private static void VerifyRawBounds()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.RecordingQuantizationEnabled = false;
        engine.ToggleRecording();
        _ = Check.Render(engine, 7.95);
        double onset = engine.BeatPosition;
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.2);
        engine.NoteOff(0, 60);
        engine.Stop();
        LoopNote wrapped = engine
            .CaptureSession()
            .Tracks[0].Notes.Single();
        Check.Near(onset, wrapped.Beat, 0, "Raw recording must retain the onset before loop wrap");
        Check.Near(
            0.4,
            wrapped.Duration,
            1e-8,
            "Raw recording must retain a duration across loop wrap"
        );
        engine.ApplySession(CreateEmptySnapshot());
        engine.ToggleRecording();
        engine.NoteOn(0, 64, 100);
        _ = Check.Render(engine, 8.1);
        engine.NoteOff(0, 64);
        engine.Stop();
        Check.Near(
            16,
            engine
                .CaptureSession()
                .Tracks[0].Notes.Single().Duration,
            0,
            "Raw durations must remain within the validated loop-duration cap"
        );

        LoopNote[] full =
        [
            .. Enumerable
                .Range(0, WorkstationEngine.MaximumNotesPerTrack)
                .Select(index => new LoopNote(index % 128, 80, 4 + (index / 128 * 0.25), 0.25))
        ];
        engine.ApplySession(Check.WithNotes(engine, 3, full));
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.01);
        engine.NoteOn(3, 36, 100);
        Check.That(
            engine.Tracks[3].NoteCount == WorkstationEngine.MaximumNotesPerTrack,
            "Raw recording must retain the per-track capacity bound."
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.03)) > 0.1,
            "A full raw-recording track must still accept live input."
        );
    }

    private static void VerifyCollisionsAndHistory()
    {
        using WorkstationEngine engine = CreateEngine();
        SessionSnapshot source = Check.WithNotes(
            engine,
            0,
            new LoopNote(60, 70, 0.13, 0.37),
            new LoopNote(65, 80, 0.125, 0.6),
            new LoopNote(60, 100, 0.14, 0.83),
            new LoopNote(72, 90, 15.875, 1.75),
            new LoopNote(72, 50, 0.01, 0.4),
            new LoopNote(75, 85, 15.9, 0.45),
            new LoopNote(77, 90, 0.375, 0.31)
        );
        TrackSnapshot[] tracks = [.. source.Tracks];
        tracks[1] = tracks[1] with { Notes = new[] { new LoopNote(48, 100, 0.11, 0.47) } };
        source = source with { Tracks = tracks };
        engine.ApplySession(source);
        long revision = engine.NoteRevision;
        Check.That(
            engine.QuantizeSelectedTrack(),
            "Off-grid notes and same-pitch collisions must produce a real edit."
        );
        Check.That(
            engine.NoteRevision == revision + 1,
            "A selected-track quantization batch must invalidate geometry once."
        );
        SessionSnapshot quantized = engine.CaptureSession();
        LoopNote[] expected =
        [
            new(65, 80, 0.25, 0.6),
            new(60, 100, 0.25, 0.83),
            new(72, 50, 0, 0.4),
            new(75, 85, 0, 0.45),
            new(77, 90, 0.5, 0.31)
        ];
        Check.That(
            quantized.Tracks[0].Notes.SequenceEqual(expected),
            "Quantise must round ties forward, wrap the end, retain the last stored collision winner, and preserve durations/order."
        );
        Check.That(
            quantized.Tracks[1].Notes.SequenceEqual(source.Tracks[1].Notes),
            "Quantise must leave other tracks untouched."
        );
        Check.That(
            engine.Undo() && !engine.CanUndo,
            "A complete Quantise action must be one history step."
        );
        VerifyNotes(source, engine.CaptureSession());
        engine.SelectedTrack = 2;
        revision = engine.NoteRevision;
        Check.That(
            !engine.QuantizeSelectedTrack() && engine.CanRedo && engine.NoteRevision == revision,
            "An empty-track no-op must preserve redo and note revision."
        );
        Check.That(engine.Redo(), "Quantise must remain redoable after an empty action.");
        engine.SelectedTrack = 0;
        revision = engine.NoteRevision;
        Check.That(
            !engine.QuantizeSelectedTrack() && engine.NoteRevision == revision,
            "Repeated Quantise must be idempotent without a new revision."
        );
        Check.That(
            engine.Undo() && !engine.CanUndo,
            "An aligned-track no-op must not add a history step."
        );
        VerifyNotes(source, engine.CaptureSession());
        Check.That(engine.Redo(), "The quantized result must remain restorable.");
        VerifyNotes(quantized, engine.CaptureSession());
        engine.Dispose();
        Check.Throws<ObjectDisposedException>(() => engine.QuantizeSelectedTrack());
        Check.Throws<ObjectDisposedException>(() => engine.RecordingQuantizationEnabled = false);
    }

    private static void VerifyHeldOwners()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.RecordingQuantizationEnabled = false;
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.07);
        engine.SetSustain(0, true);
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.09);
        engine.NoteOff(0, 60);
        double beat = engine.BeatPosition;
        Check.That(
            engine.QuantizeSelectedTrack(),
            "Quantise must finalize and align an active sustained recording."
        );
        Check.That(
            engine.IsPlaying && !engine.IsRecording,
            "Quantise must end recording while retaining playback state."
        );
        Check.Near(
            beat,
            engine.BeatPosition,
            0,
            "Quantise must retain the exact transport position"
        );
        LoopNote note = engine
            .CaptureSession()
            .Tracks[0].Notes.Single();
        Check.Near(0.25, note.Beat, 0, "The finalized held onset must snap to the grid");
        Check.Near(
            0.18,
            note.Duration,
            1e-9,
            "The finalized expressive duration must be preserved"
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 1,
            "Quantise must retain the live sustained voice."
        );
        engine.SetSustain(0, false);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Pedal release after Quantise must not strand a live or sequenced voice."
        );
        Check.That(
            engine
                .CaptureSession()
                .Tracks[0].Notes.Single()
                == note,
            "Later live release must not extend the finalized recording."
        );
        Check.That(
            engine.Undo(),
            "Quantise must remain separate from the finalized recording pass."
        );
        Check.Near(
            0.14,
            engine
                .CaptureSession()
                .Tracks[0].Notes.Single().Beat,
            1e-9,
            "Undo must restore the performed onset"
        );
        Check.That(
            engine.Undo() && engine.Tracks[0].NoteCount == 0,
            "The prior recording pass must remain the earlier history step."
        );
    }

    private static void VerifyPendingLoopEligibility()
    {
        using WorkstationEngine engine = CreateEngine();
        using WorkstationEngine live = CreateEngine();
        engine.RecordingQuantizationEnabled = false;
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.07);
        engine.NoteOn(0, 60, 100);
        live.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.01);
        _ = Check.Render(live, 0.01);
        engine.NoteOff(0, 60);
        live.NoteOff(0, 60);
        Check.That(
            engine.QuantizeSelectedTrack(),
            "A raw onset must move into the current cycle's future grid slot."
        );
        Check.That(
            Check
                .Render(engine, 0.3)
                .SequenceEqual(Check.Render(live, 0.3)),
            "Quantise must preserve stable pending-note eligibility and avoid doubling the live performance."
        );
        engine.Stop();
        engine.Play();
        Check.That(
            Check.Energy(Check.Render(engine, 0.2)) > 0.1,
            "The quantized note must replay normally after transport reset."
        );
    }

    private static void VerifyCurrentPosition()
    {
        using WorkstationEngine engine = CreateEngine();
        engine.ApplySession(
            Check.WithNotes(
                engine,
                0,
                new LoopNote(60, 100, 1.1, 0.37),
                new LoopNote(65, 100, 1.4, 0.31)
            )
        );
        engine.Play();
        _ = Check.Render(engine, 0.55);
        Check.That(
            engine.QuantizeSelectedTrack(),
            "Loaded off-grid notes must be editable during playback."
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.1)) == 0,
            "Quantise must not chase an onset moved before the current position."
        );
        _ = Check.Render(engine, 0.1);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 0.1,
            "An aligned future onset must retain its scheduled playback."
        );
    }

    private static WorkstationEngine CreateEngine()
    {
        WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static SessionSnapshot CreateEmptySnapshot()
    {
        using WorkstationEngine engine = CreateEngine();
        return engine.CaptureSession();
    }

    private static void VerifyNotes(SessionSnapshot expected, SessionSnapshot actual)
    {
        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            Check.That(
                expected.Tracks[track].Notes.SequenceEqual(actual.Tracks[track].Notes),
                "Quantise history must restore exact note content and ordering."
            );
        }
    }
}
