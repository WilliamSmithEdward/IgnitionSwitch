using IgnitionSwitch.Core.Sequencing;

internal static class AudioTests
{
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
}
