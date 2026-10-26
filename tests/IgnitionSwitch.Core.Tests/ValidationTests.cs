using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;

internal static class ValidationTests
{
    public static Task Controls()
    {
        using WorkstationEngine engine = new();
        Check.Throws<ArgumentOutOfRangeException>(() => engine.Tempo = double.NaN);
        Check.Throws<ArgumentOutOfRangeException>(() => engine.Tempo = 181);
        Check.Throws<ArgumentOutOfRangeException>(
            () => engine.MasterVolume = float.PositiveInfinity
        );
        Check.Throws<ArgumentOutOfRangeException>(() => engine.SetTrackVolume(-1, 1));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.SetPitchBend(2));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.NoteOn(128, 100));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.NoteOff(-1));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.SelectedTrack = 4);
        Check.Throws<ArgumentException>(() => engine.Render(new float[3], 0, 3));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.Render(new float[2], 0, 4));
        engine.Dispose();
        Check.Throws<ObjectDisposedException>(engine.Play);
        return Task.CompletedTask;
    }

    public static async Task Projects()
    {
        using WorkstationEngine engine = new();
        SessionSnapshot valid = engine.CaptureSession();
        Check.Throws<InvalidDataException>(() => engine.ApplySession(valid with { Version = 999 }));
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(valid with { Tempo = double.NaN })
        );
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(valid with { MasterVolume = -1 })
        );
        Check.Throws<InvalidDataException>(() => engine.ApplySession(valid with { LoopBars = 64 }));
        Check.Throws<InvalidDataException>(() => engine.ApplySession(valid with { Tracks = [] }));
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 100, 16, 0.25)))
        );
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(
                Check.WithNotes(engine, 0, new LoopNote(60, 100, 0, double.PositiveInfinity))
            )
        );
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 0, 0, 1)))
        );
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(
                Check.WithNotes(
                    engine,
                    0,
                    [
                        .. Enumerable.Repeat(
                            new LoopNote(60, 100, 0, 1),
                            WorkstationEngine.MaximumNotesPerTrack + 1
                        )
                    ]
                )
            )
        );
        Check.Near(
            valid.Tempo,
            engine.Tempo,
            0,
            "Rejected sessions must preserve the current engine"
        );

        string directory = Path.Combine(
            Path.GetTempPath(),
            "IgnitionSwitch-tests-"
                + Guid
                    .NewGuid()
                    .ToString("N")
        );
        _ = Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "malformed.ignition");
            await File.WriteAllTextAsync(path, "{this is not json}");
            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
            await File.WriteAllTextAsync(path, "null");
            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));

            await using (FileStream file = File.Create(path))
            {
                file.SetLength(SessionStore.MaximumFileBytes + 1L);
            }

            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
        }

        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
