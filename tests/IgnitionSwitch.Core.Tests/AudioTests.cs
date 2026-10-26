using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class AudioTests
{
    public static Task RenderPartitions()
    {
        using WorkstationEngine contiguous = new()
        {
            Tempo = 120,
            MetronomeEnabled = true
        };
        using WorkstationEngine partitioned = new() { MetronomeEnabled = true };
        SessionSnapshot session = Check.WithNotes(
            contiguous,
            0,
            new LoopNote(60, 100, 0, 0.125),
            new LoopNote(64, 90, 15.75, 0.5)
        );
        TrackSnapshot[] tracks = [.. session.Tracks];
        tracks[1] = tracks[1] with { Notes = [new LoopNote(43, 90, 4, 2)] };
        tracks[2] = tracks[2] with { Notes = [new LoopNote(72, 70, 8, 6)] };
        tracks[3] = tracks[3] with
        {
            Notes =
            [
                new LoopNote(36, 110, 0, 0.25),
                new LoopNote(38, 90, 2, 0.25)
            ]
        };
        session = session with { Tracks = tracks };
        contiguous.ApplySession(session);
        partitioned.ApplySession(session);
        contiguous.Play();
        partitioned.Play();
        const int Offset = 3;
        const float Sentinel = 42;
        int count = WorkstationEngine.SampleRate * 17;
        float[] wholeBuffer = [.. Enumerable.Repeat(Sentinel, count + (Offset * 2))];
        float[] splitBuffer = [.. Enumerable.Repeat(Sentinel, count + (Offset * 2))];
        contiguous.Render(wholeBuffer, Offset, count);
        int[] framesPerCallback = [1, 31, 64, 97, 113, 256];
        int written = 0;
        int callback = 0;

        while (written < count)
        {
            int samples = Math.Min(
                count - written,
                framesPerCallback[callback++ % framesPerCallback.Length] * 2
            );
            partitioned.Render(splitBuffer, Offset + written, samples);
            written += samples;
        }

        Check.That(
            wholeBuffer
                .AsSpan(Offset, count)
                .SequenceEqual(splitBuffer.AsSpan(Offset, count)),
            "Internal render quanta and external callback partitions must retain identical audio across beats and loop wrap."
        );
        Check.That(
            wholeBuffer
                .Take(Offset)
                .All(value => value == Sentinel)
                && wholeBuffer
                    .Skip(Offset + count)
                    .All(value => value == Sentinel)
                && splitBuffer
                    .Take(Offset)
                    .All(value => value == Sentinel)
                && splitBuffer
                    .Skip(Offset + count)
                    .All(value => value == Sentinel),
            "Rendering must preserve samples outside the requested offset/count."
        );
        Check.Near(
            1,
            contiguous.BeatPosition,
            1e-6,
            "Render quanta must retain the exact loop clock"
        );
        Check.Near(
            contiguous.BeatPosition,
            partitioned.BeatPosition,
            0,
            "Callback partitioning must not change transport time"
        );
        contiguous.Stop();
        contiguous.MetronomeEnabled = false;
        contiguous.NoteOn(60, 100);
        Check.That(
            Check
                .Render(contiguous, 0.005)
                .Take(128)
                .Any(value => value != 0),
            "A live command must enter the next rendered callback without an additional command queue."
        );
        contiguous.Dispose();
        Check.Throws<ObjectDisposedException>(() => contiguous.Render([], 0, 0));
        return Task.CompletedTask;
    }

    public static Task Instruments()
    {
        using WorkstationEngine engine = new();
        Check.That(
            Check.Energy(Check.Render(engine, 0.02)) == 0,
            "An untouched engine must be silent."
        );

        for (int track = 0; track < WorkstationEngine.TrackCount; track++)
        {
            engine.NoteOn(track, track == 3 ? 36 : 60, 100);
            float[] audio = Check.Render(engine, 0.15);
            Check.That(Check.Energy(audio) > 5, $"Track {track} did not produce sound.");

            for (int sample = 0; sample < audio.Length; sample += 2)
            {
                Check.That(
                    float.IsFinite(audio[sample]) && Math.Abs(audio[sample]) <= 1,
                    "Audio must remain finite and limited."
                );
                Check.That(audio[sample] == audio[sample + 1], "Channels must align.");
            }

            engine.AllNotesOff();
        }

        for (int note = 0; note < 128; note++)
        {
            engine.NoteOn(0, note, 127);
        }

        Check.That(
            Check
                .Render(engine, 0.1)
                .All(value => float.IsFinite(value) && Math.Abs(value) <= 1),
            "Voice stealing must keep dense chords safe."
        );
        return Task.CompletedTask;
    }

    public static Task Release()
    {
        VerifyShortMelodicTaps();
        using WorkstationEngine engine = new();
        engine.NoteOn(0, 60, 100);
        engine.SelectedTrack = 2;
        engine.NoteOff(60);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Changing selection must not strand a held keyboard note."
        );
        engine.SetSustain(0, true);
        engine.NoteOn(0, 60, 100);
        _ = Check.Render(engine, 0.1);
        engine.NoteOff(0, 60);
        Check.That(
            Check.Energy(Check.Render(engine, 0.2)) > 1,
            "The sustain pedal should hold the sound."
        );
        engine.SetSustain(0, false);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Pedal-up must release sustained voices."
        );
        engine.NoteOn(0, 60, 100);
        engine.NoteOn(0, 60, 90);
        engine.NoteOn(0, 60, 0);
        _ = Check.Render(engine, 0.2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Velocity-zero retrigger must release the key."
        );
        engine.NoteOn(2, 70, 127);
        engine.AllNotesOff();
        Check.That(
            Check.Energy(Check.Render(engine, 0.05)) == 0,
            "Panic must silence immediately."
        );
        return Task.CompletedTask;
    }

    private static void VerifyShortMelodicTaps()
    {
        for (int track = 0; track < 3; track++)
        {
            using WorkstationEngine immediate = new();
            using WorkstationEngine reference = new();
            using WorkstationEngine early = new();
            immediate.NoteOn(track, 60, 100);
            immediate.NoteOff(track, 60);
            float[] tap = Check.Render(immediate, 0.03);
            reference.NoteOn(track, 60, 100);
            float[] onset = Check.Render(reference, 0.004);
            reference.NoteOff(track, 60);
            float[] expected =
            [
                .. onset,
                .. Check.Render(reference, 0.026)
            ];
            Check.That(
                tap.SequenceEqual(expected),
                "An unrendered tap must retain the instrument's normal four-millisecond onset and release envelope."
            );
            Check.That(
                tap[0] == 0 && Check.Energy(tap) > 0.01,
                "A tap before the audio callback must produce sound with a smooth initial onset."
            );

            for (int sample = 2; sample < tap.Length; sample += 2)
            {
                Check.That(
                    Math.Abs(tap[sample] - tap[sample - 2]) < 0.02,
                    "A short tap must not create a full-amplitude release click."
                );
            }

            early.NoteOn(track, 60, 100);
            float[] beginning = Check.Render(early, 0.002);
            early.NoteOff(track, 60);
            early.NoteOff(track, 60);
            float[] earlyTap =
            [
                .. beginning,
                .. Check.Render(early, 0.028)
            ];
            Check.That(
                earlyTap.SequenceEqual(expected),
                "Very young and repeated releases must preserve the same bounded onset without extending it."
            );
            _ = Check.Render(immediate, 0.85);
            Check.That(
                Check.Energy(Check.Render(immediate, 0.02)) == 0,
                "A deferred short tap must eventually become silent for every melodic instrument."
            );

            immediate.NoteOn(track, 60, 100);
            _ = Check.Render(immediate, 0.02);
            immediate.NoteOff(track, 60);
            _ = Check.Render(immediate, 0.85);
            Check.That(
                Check.Energy(Check.Render(immediate, 0.02)) == 0,
                "Later releases must retain normal finite instrument tails."
            );
            immediate.NoteOn(track, 60, 100);
            immediate.NoteOff(track, 60);
            immediate.AllNotesOff();
            Check.That(
                Check.Energy(Check.Render(immediate, 0.02)) == 0,
                "Panic must cancel a deferred onset immediately."
            );

            immediate.NoteOn(track, 60, 100);
            immediate.NoteOn(track, 60, 100);
            immediate.NoteOff(track, 60);
            float[] retrigger = Check.Render(immediate, 0.03);
            Check.That(
                retrigger[0] == 0 && Check.Energy(retrigger) > 0.01,
                "An unrendered retrigger must give the new voice its own smooth onset."
            );
            _ = Check.Render(immediate, 0.85);
            Check.That(
                Check.Energy(Check.Render(immediate, 0.02)) == 0,
                "Unrendered retriggers must not strand their original or replacement voices."
            );
        }
    }
}
