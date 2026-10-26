using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class SoundTests
{
    public static Task Mixer()
    {
        using WorkstationEngine engine = Create();
        ParameterAddress pan = new(ParameterId.Pan, 0);
        engine.SetParameter(pan, 0);
        engine.NoteOn(0, 60, 100);
        float[] left = Check.Render(engine, 0.1);
        Check.That(
            ChannelEnergy(left, 0) > 1 && ChannelEnergy(left, 1) == 0,
            "Hard-left pan must actually isolate the left output."
        );
        engine.Stop();
        engine.SetParameter(pan, 1);
        engine.NoteOn(0, 60, 100);
        float[] right = Check.Render(engine, 0.1);
        Check.That(
            ChannelEnergy(right, 1) > 1 && ChannelEnergy(right, 0) == 0,
            "Hard-right pan must actually isolate the right output."
        );
        engine.Stop();
        engine.ApplySession(engine.CaptureSession());
        ParameterAddress level = new(ParameterId.Level, 0);
        engine.SetTrackVolume(0, 0.4f);
        Check.Near(
            engine.Tracks[0].Volume,
            engine.GetParameter(level),
            0,
            "Level metadata must address the existing track volume"
        );
        engine.SetParameter(level, 0.2);
        engine.CommitEdit();
        Check.That(engine.Undo(), "Old and generic volume setters must share one gesture history.");
        Check.Near(
            0.8,
            engine.GetParameter(level),
            1e-6,
            "Shared volume history must preserve the original target"
        );
        engine.ApplySession(engine.CaptureSession());

        foreach (ParameterAddress address in new[]
        {
            level,
            new(ParameterId.MasterVolume),
            new(ParameterId.Tempo),
            new(ParameterId.Attack, 2)
        })
        {
            double value = engine.GetParameter(address);
            engine.SetParameter(address, value);
            Check.That(
                !engine.CanUndo,
                "Get-to-Set roundtrips must not create phantom normalized edits."
            );
            Check.That(
                ReferenceEquals(engine.GetDescriptor(address), engine.GetDescriptor(address)),
                "Parameter descriptors must be cached."
            );
        }

        foreach (ParameterAddress invalid in new[]
        {
            new ParameterAddress((ParameterId)999),
            new(ParameterId.Level),
            new(ParameterId.Level, 4),
            new(ParameterId.DelayTime, 0)
        })
        {
            Check.Throws<ArgumentOutOfRangeException>(() => engine.SetParameter(invalid, 0.5));
        }

        foreach (double invalid in new[]
        {
            double.NaN,
            double.PositiveInfinity,
            -0.1,
            1.1
        })
        {
            Check.Throws<ArgumentOutOfRangeException>(() => engine.SetParameter(level, invalid));
        }

        Check.That(!engine.CanUndo, "Invalid parameter input must not mutate history.");
        Check.Near(
            0.004,
            PhysicalDefault(engine, ParameterId.Attack, 0),
            0,
            "Keys retain their exact native attack"
        );
        Check.Near(
            0.08,
            PhysicalDefault(engine, ParameterId.Attack, 2),
            0,
            "Atmosphere retains its exact native attack"
        );
        Check.Near(
            0.12,
            PhysicalDefault(engine, ParameterId.Release, 0),
            0,
            "Keys retain their exact native release"
        );
        Check.Near(
            0.7,
            PhysicalDefault(engine, ParameterId.Release, 2),
            0,
            "Atmosphere retains its exact native release"
        );
        Check.Near(
            1,
            PhysicalDefault(engine, ParameterId.Release, 3),
            0,
            "Drum Tail starts at native decay scale"
        );
        return Task.CompletedTask;
    }

    public static Task Instruments()
    {
        float[] neutral = Note(ParameterId.Drive, 0, 0.1);
        float[] driven = Note(ParameterId.Drive, 1, 0.1);
        Check.That(
            Difference(neutral, driven) > 1,
            "Drive must change actual synthesized samples."
        );
        float[] dark = Note(ParameterId.Tone, 0, 0.1);
        float[] bright = Note(ParameterId.Tone, 1, 0.1);
        Check.That(Difference(dark, bright) > 1, "Tone must change actual timbre.");
        float[] slow = Note(ParameterId.Attack, 1, 0.01);
        float[] native = Note(ParameterId.Drive, 0, 0.01);
        Check.That(
            Check.Energy(slow) < Check.Energy(native) * 0.1,
            "Attack must audibly reshape the note onset."
        );

        using (WorkstationEngine shortRelease = Create())
        using (WorkstationEngine longRelease = Create())
        {
            shortRelease.SetParameter(new(ParameterId.Release, 0), 0);
            longRelease.SetParameter(new(ParameterId.Release, 0), 1);

            foreach (WorkstationEngine engine in new[] { shortRelease, longRelease })
            {
                engine.NoteOn(0, 60, 100);
                _ = Check.Render(engine, 0.1);
                engine.NoteOff(0, 60);
                _ = Check.Render(engine, 0.2);
            }

            Check.That(
                Check.Energy(Check.Render(shortRelease, 0.05)) == 0
                    && Check.Energy(Check.Render(longRelease, 0.05)) > 1,
                "Release must control the actual melodic tail."
            );
        }

        using (WorkstationEngine shortTail = Create())
        using (WorkstationEngine longTail = Create())
        {
            shortTail.SetParameter(new(ParameterId.Release, 3), 0);
            longTail.SetParameter(new(ParameterId.Release, 3), 1);

            foreach (WorkstationEngine engine in new[] { shortTail, longTail })
            {
                engine.NoteOn(3, 38, 100);
                engine.NoteOff(3, 38);
                _ = Check.Render(engine, 1);
            }

            Check.That(
                Check.Energy(Check.Render(shortTail, 0.1)) == 0
                    && Check.Energy(Check.Render(longTail, 0.1)) > 0.1,
                "Drum Tail must change decay and lifetime while ignoring pad release."
            );
        }

        Check.That(
            driven.All(float.IsFinite) && bright.All(float.IsFinite),
            "Instrument controls must keep audio finite."
        );
        return Task.CompletedTask;
    }

    public static Task EffectsAndLanes()
    {
        using (WorkstationEngine delay = Create())
        {
            ConfigureDelay(delay, 0);
            delay.NoteOn(0, 60, 100);
            _ = Check.Render(delay, 0.005);
            delay.NoteOff(0, 60);
            _ = Check.Render(delay, 0.035);
            Check.That(
                Check.Energy(Check.Render(delay, 0.06)) > 0.1,
                "Send A and delay return must produce a real delayed tail after dry release."
            );
            delay.AllNotesOff();
            Check.That(
                Check.Energy(Check.Render(delay, 0.1)) == 0,
                "Panic must reset both effect lanes without stale samples."
            );
        }

        using (WorkstationEngine room = Create())
        {
            room.SetParameter(new(ParameterId.Release, 0), 0);
            room.SetParameter(new(ParameterId.SendB, 0), 1);
            room.SetParameter(new(ParameterId.ReverbReturn), 1);
            room.NoteOn(0, 60, 100);
            _ = Check.Render(room, 0.005);
            room.NoteOff(0, 60);
            _ = Check.Render(room, 0.1);
            Check.That(
                Check.Energy(Check.Render(room, 0.3)) > 0.1,
                "Send B and reverb return must produce a real diffuse tail."
            );
        }

        using WorkstationEngine mixed = Create();
        using WorkstationEngine live = Create();
        mixed.ApplySession(Check.WithNotes(mixed, 0, new LoopNote(60, 100, 0, 0.25)));

        foreach (WorkstationEngine engine in new[] { mixed, live })
        {
            ConfigureDelay(engine, 1);
            engine.SetParameter(new(ParameterId.SendA, 0), 1);
            engine.NoteOn(1, 64, 100);
        }

        mixed.Play();
        _ = Check.Render(mixed, 0.005);
        _ = Check.Render(live, 0.005);
        mixed.NoteOff(1, 64);
        live.NoteOff(1, 64);
        _ = Check.Render(mixed, 0.005);
        _ = Check.Render(live, 0.005);
        mixed.Pause();
        Check.That(
            Check
                .Render(mixed, 0.2)
                .SequenceEqual(Check.Render(live, 0.2)),
            "Pause must remove sequenced dry/wet state while preserving the exact live effect lane."
        );
        mixed.Stop();
        Check.That(
            Check.Energy(Check.Render(mixed, 0.1)) == 0,
            "Stop must reset both live and sequenced effect state."
        );
        VerifyReturnFaderRetention();
        return Task.CompletedTask;
    }

    private static void VerifyReturnFaderRetention()
    {
        foreach (bool room in new[] { false, true })
        {
            using WorkstationEngine faded = Create();
            using WorkstationEngine reference = Create();
            ParameterAddress returnAddress = new(
                room ? ParameterId.ReverbReturn : ParameterId.DelayReturn
            );

            foreach (WorkstationEngine engine in new[] { faded, reference })
            {
                engine.SetParameter(new(ParameterId.Release, 0), 0);
                engine.SetParameter(new(room ? ParameterId.SendB : ParameterId.SendA, 0), 1);
                engine.SetParameter(returnAddress, 1);
                engine.SetParameter(new(ParameterId.DelayFeedback), 0.8);
                ParameterAddress time = new(ParameterId.DelayTime);
                engine.SetParameter(
                    time,
                    engine
                        .GetDescriptor(time)
                        .ToNormalized(0.08)
                );
                ParameterAddress decay = new(ParameterId.ReverbDecay);
                engine.SetParameter(
                    decay,
                    engine
                        .GetDescriptor(decay)
                        .ToNormalized(3)
                );
                engine.NoteOn(0, 60, 100);
                _ = Check.Render(engine, 0.01);
                engine.NoteOff(0, 60);
                _ = Check.Render(engine, 0.09);
            }

            faded.SetParameter(returnAddress, 0);
            float[] firstRamp = Check.Render(faded, 0.001);
            float[] firstReference = Check.Render(reference, 0.001);
            Check.That(
                Check.Energy(firstRamp) > 0
                    && Check.Energy(firstRamp) < Check.Energy(firstReference),
                "An audible wet-only tail must use the return gain ramp instead of snapping immediately to zero."
            );
            _ = Check.Render(faded, 0.009);
            _ = Check.Render(reference, 0.009);
            Check.That(
                Check.Energy(Check.Render(faded, 0.24)) == 0,
                "A zero effect return must remain silent after its gain ramp."
            );
            Check.That(
                Check.Energy(Check.Render(reference, 0.24)) > 0.01,
                "The uninterrupted reference must still contain fed effect state."
            );
            faded.SetParameter(returnAddress, 1);
            _ = Check.Render(faded, 0.01);
            _ = Check.Render(reference, 0.01);
            float[] resumed = Check.Render(faded, 0.12);
            float[] expected = Check.Render(reference, 0.12);
            Check.That(
                Check.Energy(expected) > 0.01 && resumed.SequenceEqual(expected),
                "Raising a quiet-input echo or room return must reveal its continuously evolved tail, matching uninterrupted state after gain smoothing."
            );
        }
    }

    private static void ConfigureDelay(WorkstationEngine engine, int track)
    {
        ParameterAddress time = new(ParameterId.DelayTime);
        engine.SetParameter(
            time,
            engine
                .GetDescriptor(time)
                .ToNormalized(0.05)
        );
        engine.SetParameter(new(ParameterId.DelayFeedback), 0);
        engine.SetParameter(new(ParameterId.DelayDamping), 0);
        engine.SetParameter(new(ParameterId.DelayReturn), 1);
        engine.SetParameter(new(ParameterId.Release, track), 0);
        engine.SetParameter(new(ParameterId.SendA, track), 1);
    }

    private static WorkstationEngine Create()
    {
        WorkstationEngine engine = new();
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static double PhysicalDefault(WorkstationEngine engine, ParameterId id, int track)
    {
        ParameterDescriptor descriptor = engine.GetDescriptor(new(id, track));
        return descriptor.ToPhysical(engine.GetParameter(new(id, track)));
    }

    private static float[] Note(ParameterId id, double value, double seconds)
    {
        using WorkstationEngine engine = Create();
        engine.SetParameter(new(id, 0), value);
        engine.NoteOn(0, 72, 100);
        return Check.Render(engine, seconds);
    }

    private static double ChannelEnergy(float[] samples, int channel)
    {
        double energy = 0;

        for (int sample = channel; sample < samples.Length; sample += 2)
        {
            energy += Math.Abs(samples[sample]);
        }

        return energy;
    }

    private static double Difference(float[] first, float[] second)
    {
        double difference = 0;

        for (int sample = 0; sample < first.Length; sample++)
        {
            difference += Math.Abs(first[sample] - second[sample]);
        }

        return difference;
    }
}
