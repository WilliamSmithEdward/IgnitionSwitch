using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class VoicePriorityTests
{
    public static Task LiveAttacks()
    {
        using (WorkstationEngine protectedHit = Create())
        using (WorkstationEngine expected = Create())
        using (WorkstationEngine loopOnly = Create())
        {
            ApplyChord(protectedHit, Enumerable.Range(24, 64));
            ApplyChord(expected, Enumerable.Range(25, 63));
            ApplyChord(loopOnly, Enumerable.Range(24, 64));
            protectedHit.Play();
            expected.Play();
            loopOnly.Play();
            protectedHit.NoteOn(3, 37, 127);
            protectedHit.NoteOff(3, 37);
            expected.NoteOn(3, 37, 127);
            expected.NoteOff(3, 37);
            float[] actual = Check.Render(protectedHit, 0.02);
            float[] bounded = Check.Render(expected, 0.02);
            float[] baseline = Check.Render(loopOnly, 0.02);
            Check.That(
                actual.SequenceEqual(bounded),
                "A new live pad plus a 64-note scheduled chord must retain the live attack and exactly 63 oldest-priority loop voices."
            );
            Check.That(
                !actual.SequenceEqual(baseline),
                "The protected pad attack must actually reach rendered output instead of disappearing before its first sample."
            );
            Check.That(
                actual.All(value => float.IsFinite(value) && Math.Abs(value) <= 1),
                "Voice priority must retain finite limited output."
            );
        }

        using (WorkstationEngine scheduled = Create())
        using (WorkstationEngine liveOnly = Create())
        {
            ApplyChord(scheduled, Enumerable.Range(24, 64));
            scheduled.Play();
            liveOnly.Play();

            for (int note = 32; note < 96; note++)
            {
                scheduled.NoteOn(1, note, 100);
                liveOnly.NoteOn(1, note, 100);
            }

            Check.That(
                Check
                    .Render(scheduled, 0.02)
                    .SequenceEqual(Check.Render(liveOnly, 0.02)),
                "Scheduled onsets must not steal any of 64 fresh live attacks when the pool has no loop voice to replace."
            );
        }

        using (WorkstationEngine scheduled = Create())
        using (WorkstationEngine expected = Create())
        {
            scheduled.ApplySession(Check.WithNotes(scheduled, 0, new LoopNote(100, 100, 0.25, 1)));
            expected.ApplySession(Check.WithNotes(expected, 0, new LoopNote(100, 100, 0.25, 1)));
            scheduled.Play();
            expected.Play();

            for (int note = 32; note < 96; note++)
            {
                scheduled.NoteOn(1, note, 100);

                if (note > 32)
                {
                    expected.NoteOn(1, note, 100);
                }
            }

            _ = Check.Render(scheduled, 0.125);
            _ = Check.Render(expected, 0.125);
            Check.That(
                Check
                    .Render(scheduled, 0.02)
                    .SequenceEqual(Check.Render(expected, 0.02)),
                "A later scheduled onset must deterministically replace the oldest live voice after attack protection expires."
            );
        }

        using (WorkstationEngine overflow = Create())
        using (WorkstationEngine bounded = Create())
        {
            for (int note = 0; note < 70; note++)
            {
                overflow.NoteOn(0, note, 100);

                if (note >= 6)
                {
                    bounded.NoteOn(0, note, 100);
                }
            }

            Check.That(
                Check
                    .Render(overflow, 0.02)
                    .SequenceEqual(Check.Render(bounded, 0.02)),
                "More than 64 simultaneous live notes must retain the exact newest 64, rather than growing the pool."
            );
        }

        foreach (int note in new[] { 36, 37, 38, 42 })
        {
            using WorkstationEngine retrigger = Create();
            using WorkstationEngine original = Create();
            retrigger.NoteOn(3, note, 100);
            original.NoteOn(3, note, 100);
            retrigger.NoteOff(3, note);
            original.NoteOff(3, note);
            _ = Check.Render(retrigger, 0.001);
            _ = Check.Render(original, 0.001);
            retrigger.NoteOn(3, note, 100);
            retrigger.NoteOff(3, note);
            Check.That(
                !Check
                    .Render(retrigger, 0.02)
                    .SequenceEqual(Check.Render(original, 0.02)),
                "Ordinary same-note drum retriggers must still add a fresh audible voice."
            );
        }

        return Task.CompletedTask;
    }

    private static WorkstationEngine Create()
    {
        WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    private static void ApplyChord(WorkstationEngine engine, IEnumerable<int> notes)
    {
        engine.ApplySession(
            Check.WithNotes(engine, 0, [.. notes.Select(note => new LoopNote(note, 70, 0, 1))])
        );
    }
}
