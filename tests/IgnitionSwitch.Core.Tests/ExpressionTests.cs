using System.Runtime.CompilerServices;
using System.Text.Json;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class ExpressionTests
{
    public static Task NeutralAndValidation()
    {
        using WorkstationEngine ordinary = new();
        using WorkstationEngine explicitZero = new();
        var handles = new LiveNoteHandle[64];

        for (int index = 0; index < handles.Length; index++)
        {
            ordinary.NoteOn(index % 4, 36 + index, 100);
            handles[index] = explicitZero.BeginLiveNote(index % 4, 36 + index, 100);
            Check.That(
                handles[index].IsValid && explicitZero.SetLiveExpression(handles[index], 0, 0),
                "Each fresh gate must accept explicit neutral expression."
            );
        }

        Check.That(
            Check
                .Render(ordinary, 0.2)
                .SequenceEqual(Check.Render(explicitZero, 0.2)),
            "Neutral expression must preserve exact rendered audio."
        );

        for (int index = 0; index < handles.Length; index++)
        {
            ordinary.NoteOff(index % 4, 36 + index);
            explicitZero.NoteOff(index % 4, 36 + index);
        }

        Check.That(
            Check
                .Render(ordinary, 1.6)
                .SequenceEqual(Check.Render(explicitZero, 1.6)),
            "Neutral release and drum tails must remain exactly unchanged."
        );
        Check.That(
            !default(LiveNoteHandle).IsValid && !explicitZero.SetLiveExpression(default, 0, 0),
            "Default handles must never identify a voice."
        );
        Check.That(
            !ordinary.SetLiveExpression(handles[0], 1, 1),
            "Another engine must reject an issued handle."
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => ordinary.SetLiveExpression(default, double.NaN, 0)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => ordinary.SetLiveExpression(default, 0, double.PositiveInfinity)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => ordinary.BeginLiveNote(0, 60, 100, modulation: -0.01)
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => ordinary.BeginLiveNote(0, 60, 100, pressure: 1.01)
        );
        Check.Throws<ArgumentOutOfRangeException>(() => ordinary.BeginLiveNote(4, 60, 100));
        Check.Throws<ArgumentOutOfRangeException>(() => ordinary.BeginLiveNote(0, 128, 100));
        Check.Throws<ArgumentOutOfRangeException>(() => ordinary.BeginLiveNote(0, 60, 128));
        using WorkstationEngine transient = new();
        string before = JsonSerializer.Serialize(transient.CaptureSession());
        long revision = transient.NoteRevision;
        LiveNoteHandle active = transient.BeginLiveNote(0, 60, 100, modulation: 0.3, pressure: 0.5);
        _ = Check.Render(transient, 0.1);
        Check.That(
            transient.SetLiveExpression(active, 1, 1)
                && !transient.CanUndo
                && transient.NoteRevision == revision
                && JsonSerializer.Serialize(transient.CaptureSession()) == before,
            "Live expression must not change content, parameters, schema, revisions, or Undo history."
        );
        LiveNoteHandle released = transient.BeginLiveNote(0, 60, 0);
        Check.That(
            !released.IsValid && !transient.SetLiveExpression(active, 1, 1),
            "Velocity zero must release the existing gate and return default."
        );
        return Task.CompletedTask;
    }

    public static Task MusicalResponse()
    {
        using (WorkstationEngine vibrato = new())
        {
            _ = vibrato.BeginLiveNote(1, 69, 100, modulation: 1);
            float[] audio = Check.Render(vibrato, 2);
            List<double> crossings = PositiveCrossings(audio, 0.2);
            List<(double Time, double Cents)> pitch = [];

            for (int index = 1; index < crossings.Count; index++)
            {
                double frequency = WorkstationEngine.SampleRate
                    / (crossings[index] - crossings[index - 1]);
                pitch.Add(
                    (
                        (crossings[index] + crossings[index - 1])
                            / (2 * WorkstationEngine.SampleRate),
                        1200 * Math.Log2(frequency / 440)
                    )
                );
            }

            Check.That(
                pitch.Max(value => value.Cents) is > 29
                    and < 30.5 && pitch.Min(value => value.Cents) is < -29
                    and > -30.5,
                "Rendered vibrato must reach approximately plus/minus30cents without exceeding its bound."
            );
            List<double> periods = [];
            double previous = 0;

            for (int index = 1; index < pitch.Count; index++)
            {
                if (pitch[index - 1].Cents < 0 && pitch[index].Cents >= 0)
                {
                    double time = pitch[index].Time;

                    if (previous != 0)
                    {
                        periods.Add(time - previous);
                    }

                    previous = time;
                }
            }

            Check.Near(
                5.5,
                1 / periods.Average(),
                0.1,
                "Actual rendered pitch must oscillate at5.5Hz"
            );
        }

        for (int track = 0; track < 4; track++)
        {
            using WorkstationEngine neutral = new();
            using WorkstationEngine pressured = new();
            int note = track == 3 ? 36 : 60;
            _ = neutral.BeginLiveNote(track, note, 100);
            _ = pressured.BeginLiveNote(track, note, 100, pressure: 1);
            float[] baseline = Check.Render(neutral, 0.2);
            float[] actual = Check.Render(pressured, 0.2);
            Check.That(
                Check.Energy(actual) > Check.Energy(baseline) * 1.01
                    && !actual.SequenceEqual(baseline),
                "Pressure must affect real audio for every live instrument, including drums."
            );
            Check.That(
                actual.All(value => float.IsFinite(value) && Math.Abs(value) <= 1),
                "Expression output must remain finite and limited."
            );
            Check.That(
                pressured.GetParameter(new(ParameterId.Tone, track)) == 0.5
                    && pressured.GetParameter(new(ParameterId.Level, track)) == 0.8f,
                "Expression must not replace saved Tone or Level targets."
            );

            if (track == 0)
            {
                double baseRatio = HarmonicRatio(baseline, 261.6255653005986);
                double pressureRatio = HarmonicRatio(actual, 261.6255653005986);
                Check.That(
                    pressureRatio > baseRatio * 1.01,
                    "Pressure must brighten harmonic balance beyond a gain-only alias."
                );
            }
        }

        using WorkstationEngine drums = new();
        using WorkstationEngine dryDrums = new();
        _ = drums.BeginLiveNote(3, 36, 100, modulation: 1);
        dryDrums.NoteOn(3, 36, 100);
        Check.That(
            Check
                .Render(drums, 0.2)
                .SequenceEqual(Check.Render(dryDrums, 0.2)),
            "Wheel vibrato must leave drum pitch unchanged."
        );
        VerifyPressureRamp();
        VerifyIndependentVoices();
        return Task.CompletedTask;
    }

    public static Task OwnershipAndLifecycle()
    {
        using WorkstationEngine engine = new();
        LiveNoteHandle first = engine.BeginLiveNote(0, 60, 100);
        LiveNoteHandle replacement = engine.BeginLiveNote(0, 60, 100);
        Check.That(
            !engine.SetLiveExpression(first, 1, 1)
                && engine.SetLiveExpression(replacement, 0.5, 0.5),
            "A stale retrigger handle must not control its replacement or old release tail."
        );
        engine.SetSustain(0, true);
        engine.NoteOff(0, 60);
        Check.That(
            engine.SetLiveExpression(replacement, 1, 1),
            "A sustained gate remains the same live owner after key-up."
        );
        engine.Play();
        engine.Pause();
        Check.That(engine.TryCreateScene("Other", out int scene), "A scene should be available.");
        engine.QueueSceneLaunch(scene);
        engine.Play();
        _ = Check.Render(engine, 2.5);
        Check.That(
            engine.SetLiveExpression(replacement, 0.25, 0.25),
            "Pause and scene launch must preserve sustained live identity."
        );
        Check.That(
            engine.Undo() && engine.SetLiveExpression(replacement, 0.25, 0.25),
            "Undo must preserve a sustained live gate while restoring content."
        );
        engine.SetSustain(0, false);
        Check.That(
            !engine.SetLiveExpression(replacement, 1, 1),
            "Real pedal release must invalidate the gate before its tail finishes."
        );
        LiveNoteHandle drum = engine.BeginLiveNote(3, 36, 100);
        LiveNoteHandle newDrum = engine.BeginLiveNote(3, 36, 100);
        Check.That(
            !engine.SetLiveExpression(drum, 0, 1) && engine.SetLiveExpression(newDrum, 0, 1),
            "One-shot retriggers must retain only the latest gate for pressure."
        );
        engine.NoteOff(3, 36);
        Check.That(
            !engine.SetLiveExpression(newDrum, 0, 1),
            "Drum key-up ends pressure ownership without stopping its shot."
        );
        engine.AllNotesOff();
        LiveNoteHandle stolen = engine.BeginLiveNote(1, 24, 100);

        for (int note = 25; note < 89; note++)
        {
            _ = engine.BeginLiveNote(1, note, 100);
        }

        Check.That(
            !engine.SetLiveExpression(stolen, 1, 1),
            "A stolen voice cannot be resurrected through expression."
        );
        engine.AllNotesOff();
        LiveNoteHandle panic = engine.BeginLiveNote(0, 65, 100);
        engine.AllNotesOff();
        Check.That(!engine.SetLiveExpression(panic, 1, 1), "Panic must invalidate issued gates.");
        LiveNoteHandle stopped = engine.BeginLiveNote(0, 65, 100);
        engine.Stop();
        Check.That(!engine.SetLiveExpression(stopped, 1, 1), "Stop must invalidate issued gates.");
        LiveNoteHandle loaded = engine.BeginLiveNote(0, 65, 100);
        engine.ApplySession(engine.CaptureSession());
        Check.That(
            !engine.SetLiveExpression(loaded, 1, 1),
            "Project replacement must invalidate issued gates."
        );
        LiveNoteHandle finished = engine.BeginLiveNote(3, 36, 100);
        _ = Check.Render(engine, 1.6);
        Check.That(
            !engine.SetLiveExpression(finished, 0, 1),
            "A naturally finished one-shot must not accept pressure."
        );
        engine.Dispose();
        Check.Throws<ObjectDisposedException>(() => engine.SetLiveExpression(finished, 0, 0));
        return Task.CompletedTask;
    }

    public static Task PartitionsAndAllocation()
    {
        using WorkstationEngine whole = new();
        using WorkstationEngine split = new();
        LiveNoteHandle wholeHandle = whole.BeginLiveNote(1, 69, 100);
        LiveNoteHandle splitHandle = split.BeginLiveNote(1, 69, 100);
        float[] expected = new float[9600];
        float[] actual = new float[expected.Length];

        for (int block = 0; block < 5; block++)
        {
            double value = block % 2 == 0 ? 1 : 0;
            _ = whole.SetLiveExpression(wholeHandle, value, value);
            _ = split.SetLiveExpression(splitHandle, value, value);
            int start = block * 1920;
            whole.Render(expected, start, 1920);

            for (int offset = start; offset < start + 1920;)
            {
                int count = Math.Min(146, start + 1920 - offset);
                split.Render(actual, offset, count);
                offset += count;
            }
        }

        Check.That(
            actual.SequenceEqual(expected) && actual.All(float.IsFinite),
            "Expression ramps and the free-running LFO must be exactly callback-partition invariant."
        );
        using WorkstationEngine bounded = new();
        var handles = new LiveNoteHandle[64];

        for (int note = 0; note < handles.Length; note++)
        {
            handles[note] = bounded.BeginLiveNote(
                note % 3,
                24 + note,
                100,
                modulation: 1,
                pressure: 1
            );
        }

        float[] buffer = new float[960];
        _ = MeasureWarmLoop(bounded, handles, buffer, 256);
        Check.That(
            MeasureWarmLoop(bounded, handles, buffer, 256) == 0,
            "Warm fixed-voice rendering and expression retargeting must allocate no objects."
        );
        Check.That(
            buffer.All(value => float.IsFinite(value) && Math.Abs(value) <= 1),
            "Dense expression must remain finite and limited."
        );
        return Task.CompletedTask;
    }

    private static void VerifyPressureRamp()
    {
        using WorkstationEngine ramped = new();
        using WorkstationEngine neutral = new();
        using WorkstationEngine full = new();

        foreach (WorkstationEngine engine in new[] { ramped, neutral, full })
        {
            engine.SetParameter(new(ParameterId.Tone, 1), 1);
        }

        LiveNoteHandle handle = ramped.BeginLiveNote(1, 69, 100);
        neutral.NoteOn(1, 69, 100);
        _ = full.BeginLiveNote(1, 69, 100, pressure: 1);
        _ = Check.Render(ramped, 0.1);
        _ = Check.Render(neutral, 0.1);
        _ = Check.Render(full, 0.1);
        _ = ramped.SetLiveExpression(handle, 0, 1);
        float[] actual = Check.Render(ramped, 0.005);
        float[] baseline = Check.Render(neutral, 0.005);
        float[] endpoint = Check.Render(full, 0.005);

        for (int sample = 0; sample < actual.Length; sample++)
        {
            double amount = ((sample / 2) + 1) / 240.0;
            double reference = Math.Tanh(Math.Atanh(baseline[sample]) * (1 + (0.15 * amount)));
            Check.Near(
                reference,
                actual[sample],
                0.000001,
                "Actual pressure gain must ramp smoothly across exactly5ms"
            );
        }

        Check.Near(
            endpoint[^1],
            actual[^1],
            0.000001,
            "The pressure ramp must finish at the bounded15percent gain endpoint"
        );
    }

    private static void VerifyIndependentVoices()
    {
        using WorkstationEngine combined = new();
        using WorkstationEngine expressOnly = new();
        using WorkstationEngine neutralOnly = new();
        _ = combined.BeginLiveNote(0, 60, 100, modulation: 1, pressure: 1);
        _ = combined.BeginLiveNote(0, 67, 100);
        _ = expressOnly.BeginLiveNote(0, 60, 100, modulation: 1, pressure: 1);
        neutralOnly.NoteOn(0, 67, 100);
        float[] mix = Check.Render(combined, 0.2);
        float[] expressed = Check.Render(expressOnly, 0.2);
        float[] unchanged = Check.Render(neutralOnly, 0.2);
        double maximumError = 0;

        for (int sample = 0; sample < mix.Length; sample++)
        {
            double reference = Math.Tanh(
                Math.Atanh(expressed[sample]) + Math.Atanh(unchanged[sample])
            );
            maximumError = Math.Max(maximumError, Math.Abs(reference - mix[sample]));
        }

        Check.That(
            maximumError < 0.000001,
            "Per-handle expression must leave another live pitch on the same track unchanged."
        );
        using WorkstationEngine loop = new();
        using WorkstationEngine referenceLoop = new();
        SessionSnapshot session = Check.WithNotes(loop, 1, new LoopNote(69, 100, 0, 8));
        loop.ApplySession(session);
        referenceLoop.ApplySession(session);
        loop.SetTrackMuted(0, true);
        referenceLoop.SetTrackMuted(0, true);
        loop.Play();
        referenceLoop.Play();
        _ = loop.BeginLiveNote(0, 60, 100, modulation: 1, pressure: 1);
        _ = referenceLoop.BeginLiveNote(0, 60, 100);
        Check.That(
            Check
                .Render(loop, 0.5)
                .SequenceEqual(Check.Render(referenceLoop, 0.5)),
            "Live expression must not alter a simultaneously sequenced voice."
        );
    }

    private static List<double> PositiveCrossings(float[] stereo, double startSeconds)
    {
        List<double> result = [];

        for (
            int frame = (int)(startSeconds * WorkstationEngine.SampleRate) + 1;
            frame < stereo.Length / 2;
            frame++
        )
        {
            float previous = stereo[(frame - 1) * 2];
            float current = stereo[frame * 2];

            if (previous < 0 && current >= 0)
            {
                result.Add(frame - 1 + (-previous / (double)(current - previous)));
            }
        }

        return result;
    }

    private static double HarmonicRatio(float[] stereo, double frequency)
    {
        double fundamental = Spectrum(stereo, frequency);
        return (Spectrum(stereo, frequency * 2) + Spectrum(stereo, frequency * 3)) / fundamental;
    }

    private static double Spectrum(float[] stereo, double frequency)
    {
        double sine = 0;
        double cosine = 0;
        int first = WorkstationEngine.SampleRate / 50;
        int frames = stereo.Length / 2;

        for (int frame = first; frame < frames; frame++)
        {
            double window = 0.5
                - (0.5 * Math.Cos(2 * Math.PI * (frame - first) / (frames - first - 1)));
            double sample = stereo[frame * 2] * window;
            double phase = 2 * Math.PI * frequency * frame / WorkstationEngine.SampleRate;
            sine += sample * Math.Sin(phase);
            cosine += sample * Math.Cos(phase);
        }

        return Math.Sqrt((sine * sine) + (cosine * cosine));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureWarmLoop(
        WorkstationEngine engine,
        LiveNoteHandle[] handles,
        float[] buffer,
        int count
    )
    {
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < count; index++)
        {
            double value = index % 2 == 0 ? 1 : 0.25;
            _ = engine.SetLiveExpression(handles[index % handles.Length], value, value);
            engine.Render(buffer, 0, buffer.Length);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
