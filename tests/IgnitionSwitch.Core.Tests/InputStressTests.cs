using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class InputStressTests
{
    public static Task OrderedInputs()
    {
        VerifyIncrementalPlayback();
        VerifyBurstReleases();
        VerifyCapacityAndRevisions();
        return Task.CompletedTask;
    }

    private static void VerifyIncrementalPlayback()
    {
        using WorkstationEngine recorded = new() { Tempo = 120 };
        using WorkstationEngine loaded = new();
        recorded.ToggleRecording();

        for (int step = 0; step < 8; step++)
        {
            for (int track = 0; track < 3; track++)
            {
                recorded.NoteOn(track, 48 + (track * 5), 90);
                recorded.NoteOn(track, 52 + (track * 5), 100);
            }

            recorded.NoteOn(3, 36, 100);
            recorded.NoteOn(3, 38, 80);
            _ = Check.Render(recorded, 0.125);

            for (int track = 2; track >= 0; track--)
            {
                recorded.NoteOff(track, 52 + (track * 5));
                recorded.NoteOff(track, 48 + (track * 5));
            }
        }

        recorded.ToggleRecording();
        Check.That(
            recorded.Tracks.All(track => track.NoteCount == 16),
            "Ordered chord and drum bursts must retain every distinct quantized note."
        );
        CompareRecordedAndLoaded(recorded, loaded);
        recorded.ToggleRecording();
        recorded.NoteOn(0, 48, 80);
        _ = Check.Render(recorded, 0.25);
        recorded.NoteOff(0, 48);
        recorded.Pause();
        LoopNote replacement = recorded
            .CaptureSession()
            .Tracks[0].Notes.Single(note => note.Note == 48 && note.Beat == 0);
        Check.That(
            replacement.Duration == 0.5
                && replacement.Velocity == 80
                && recorded.Tracks[0].NoteCount == 16,
            "Incremental overdub must replace one note pair without losing unrelated notes."
        );
        CompareRecordedAndLoaded(recorded, loaded);
    }

    private static void CompareRecordedAndLoaded(
        WorkstationEngine recorded,
        WorkstationEngine loaded
    )
    {
        recorded.Stop();
        loaded.ApplySession(recorded.CaptureSession());
        recorded.Play();
        loaded.Play();
        float[] incremental = Check.Render(recorded, 3);
        float[] rebuilt = Check.Render(loaded, 3);
        Check.That(incremental.All(float.IsFinite), "Recorded bursts must render finite samples.");

        for (int sample = 0; sample < incremental.Length; sample++)
        {
            Check.Near(
                rebuilt[sample],
                incremental[sample],
                1e-6,
                "Incremental schedule playback must match a freshly loaded schedule"
            );
        }

        Check.That(
            Check.Energy(Check.Render(recorded, 0.1)) == 0,
            "All paired burst notes must reach their scheduled release."
        );
        recorded.Stop();
        loaded.Stop();
    }

    private static void VerifyBurstReleases()
    {
        using WorkstationEngine engine = new();
        engine.SetSustain(0, true);

        for (int repeat = 0; repeat < 32; repeat++)
        {
            for (int note = 48; note < 96; note++)
            {
                engine.NoteOn(0, note, 100);
            }

            for (int note = 95; note >= 48; note--)
            {
                engine.NoteOff(0, note);
            }
        }

        engine.SetSustain(0, false);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Rapid ordered retriggers and pedal release must not strand voices after voice stealing."
        );
    }

    private static void VerifyCapacityAndRevisions()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        LoopNote[] notes =
        [
            .. Enumerable
                .Range(0, WorkstationEngine.MaximumNotesPerTrack)
                .Select(index => new LoopNote(index / 64, 80, index % 64 * 0.25, 0.25))
        ];
        engine.ApplySession(Check.WithNotes(engine, 3, notes));
        long revision = engine.NoteRevision;
        engine.MasterVolume = 0.7f;
        engine.SetTrackVolume(0, 0.5f);
        engine.SetTrackMuted(1, true);
        engine.SelectedTrack = 3;
        Check.That(
            engine.NoteRevision == revision,
            "Mixer and selection changes must not invalidate recorded note geometry."
        );
        engine.ToggleRecording();
        engine.NoteOn(3, 127, 100);
        Check.That(
            engine.Tracks[3].NoteCount == WorkstationEngine.MaximumNotesPerTrack
                && engine.NoteRevision == revision,
            "A full recording track must stay bounded without inventing a content revision."
        );
        engine.ClearSelectedTrack();
        Check.That(
            engine.NoteRevision > revision && engine.Tracks[3].NoteCount == 0,
            "Clearing recorded notes must advance the note revision."
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) > 1,
            "Live input must still register even when its recording track was full."
        );
        engine.Stop();
        engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 0, 0.25)));
        revision = engine.NoteRevision;
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.125);
        engine.NoteOff(0, 60);
        Check.That(
            engine.NoteRevision == revision,
            "An identical note replacement must preserve the content revision."
        );
        engine.Stop();
        engine.ToggleRecording();
        engine.NoteOn(0, 60, 99);
        _ = Check.Render(engine, 0.125);
        engine.NoteOff(0, 60);
        Check.That(
            engine.NoteRevision > revision,
            "A changed velocity must advance the content revision."
        );
    }
}
