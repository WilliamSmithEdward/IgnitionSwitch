using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;

internal static class PersistenceTests
{
    public static async Task Roundtrip()
    {
        using WorkstationEngine engine = new();
        LoopNote[] source = [new LoopNote(60, 100, 0, 1)];
        engine.ApplySession(Check.WithNotes(engine, 0, source));
        source[0] = new LoopNote(0, 1, 0, 1);
        Check.That(
            engine.CaptureSession().Tracks[0].Notes[0].Note == 60,
            "Applying a project must detach from mutable caller-owned collections."
        );
        SessionSnapshot snapshot = engine.CaptureSession();
        engine.SelectedTrack = 0;
        engine.ClearSelectedTrack();
        Check.That(
            snapshot.Tracks[0].Notes.Count == 1,
            "Snapshots must survive later engine edits."
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
            string path = Path.Combine(directory, "song.ignition");
            await SessionStore.SaveAsync(path, snapshot);
            SessionSnapshot loaded = await SessionStore.LoadAsync(path);
            Check.That(
                loaded.Tracks[0].Notes.Single() == snapshot.Tracks[0].Notes.Single(),
                "Project save/load must retain MIDI events."
            );
            Check.Near(snapshot.Tempo, loaded.Tempo, 0, "Project save/load must retain tempo");
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Check.ThrowsAsync<OperationCanceledException>(
                () => SessionStore.SaveAsync(
                    path,
                    snapshot with { Tempo = 130 },
                    cancellation.Token
                )
            );
            Check.Near(
                snapshot.Tempo,
                (await SessionStore.LoadAsync(path)).Tempo,
                0,
                "Cancellation must preserve the existing project"
            );
            Check.That(
                Directory.GetFiles(directory).Length == 1,
                "Temporary save files must be cleaned up."
            );
            await SessionStore.SaveAsync(path, snapshot with { Tempo = 130 });
            Check.Near(
                130,
                (await SessionStore.LoadAsync(path)).Tempo,
                0,
                "Atomic replacement must retain the new project"
            );
        }

        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
