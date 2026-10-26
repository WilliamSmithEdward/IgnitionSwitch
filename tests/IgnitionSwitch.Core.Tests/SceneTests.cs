using System.Text.Json.Nodes;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;

internal static class SceneTests
{
    public static Task ContentAndHistory()
    {
        using WorkstationEngine engine = new();
        Check.That(engine.Scenes is [{ Id: 0 }], "A new project must have exactly one real scene.");
        engine.ApplySession(Check.WithNotes(engine, 0, new LoopNote(60, 90, 1, 0.5)));
        (
            string? trackName,
            InstrumentKind instrument,
            bool muted,
            float volume,
            IReadOnlyList<LoopNote>? legacyNotes,
            TrackSoundSettings? sound
        ) = engine.CaptureSession().Tracks[0];
        (
            int version,
            double tempo,
            int bars,
            float master,
            IReadOnlyList<TrackSnapshot>? sourceTracks,
            DelaySettings? delay,
            ReverbSettings? reverb
        ) = engine.CaptureSession();
        Check.That(
            trackName == engine.Tracks[0].Name
                && instrument == InstrumentKind.Keys
                && !muted
                && volume > 0
                && legacyNotes.Count == 1
                && sound is not null
                && version == 3
                && tempo > 0
                && bars == 4
                && master > 0
                && sourceTracks.Count == 4
                && delay is not null
                && reverb is not null,
            "Legacy deconstruction must remain source compatible."
        );
        Check.That(
            engine.TryDuplicateScene(0, "Variation", out int variation),
            "A bounded duplicate must succeed."
        );
        Check.That(
            engine.EditingSceneId == variation
                && engine
                    .GetClipNotes(new(0, variation))
                    .Single().Beat
                    == 1,
            "Duplication must select detached content without replacing mixer state."
        );
        Check.That(
            engine.Undo() && engine.Scenes.Count == 1 && engine.Redo(),
            "Structural duplication must undo and redo."
        );
        engine.RenameScene(variation, "Chorus");
        Check.That(
            engine.Undo() && engine.Scenes[1].Name == "Variation" && engine.Redo(),
            "Scene rename must have reversible history."
        );
        engine.EditingSceneId = 0;
        Check.That(
            engine.ClearClip(new(0, variation)) && engine.GetClipNotes(new(0, 0)).Count == 1,
            "Exact clip clear must preserve selection and other scenes."
        );
        long revision = engine.NoteRevision;
        Check.That(
            !engine.ClearClip(new(0, variation)) && engine.NoteRevision == revision,
            "Empty clear must be a no-op."
        );
        Check.That(
            engine.Undo() && engine.GetClipNotes(new(0, variation)).Count == 1,
            "Clear must restore only its addressed clip."
        );
        Check.That(
            engine.DeleteScene(0) && engine.CaptureSession().Tracks[0].Notes.Count == 0,
            "Deleting scene zero must leave an empty compatibility projection."
        );
        Check.That(!engine.DeleteScene(variation), "The final scene must remain.");
        Check.That(
            engine.Undo()
                && engine.Scenes[0].Id == 0
                && engine.CaptureSession().Tracks[0].Notes.Count == 1,
            "Deletion undo must restore slot, order, and content."
        );

        for (int index = engine.Scenes.Count; index < WorkstationEngine.MaximumScenes; index++)
        {
            Check.That(
                engine.TryCreateScene($"Scene {index + 1}", out _),
                "Available scene slots must be usable."
            );
        }

        Check.That(
            !engine.TryCreateScene("Overflow", out _) && engine.Scenes.Count == 8,
            "Scene count must be bounded."
        );
        Check.Throws<ArgumentException>(() => engine.RenameScene(0, "\n"));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.GetClipNotes(new(4, 0)));
        Check.Throws<ArgumentOutOfRangeException>(() => engine.GetClipNotes(new(0, 99)));
        return Task.CompletedTask;
    }

    public static async Task PersistenceAndCapacity()
    {
        using WorkstationEngine engine = new();
        SessionSnapshot project = TwoScenes(engine, new(60, 100, 0.5, 0.25), new(72, 100, 2, 0.75));
        engine.ApplySession(project);
        engine.EditingSceneId = 1;
        engine.QueueSceneLaunch(1);
        engine.SetTrackSolo(2, true);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"IgnitionSwitch-scenes-{Guid.NewGuid():N}.json"
        );

        try
        {
            await SessionStore.SaveAsync(path, engine.CaptureSession());
            string json = await File.ReadAllTextAsync(path);
            JsonNode node = JsonNode.Parse(json)!;
            Check.That(
                node["version"]!.GetValue<int>() == 3 && node["tracks"]![0]!["notes"] is null,
                "Schema three must serialize clip-owned notes exactly once."
            );
            using WorkstationEngine loaded = new();
            loaded.ApplySession(await SessionStore.LoadAsync(path));
            Check.That(
                loaded.Scenes.Count == 2
                    && loaded.EditingSceneId == 0
                    && !loaded.GetTrackSolo(2)
                    && loaded.GetTrackPlaybackState(0).ActiveSceneId == 0
                    && !loaded.IsPlaying,
                "Load must prepare the first scene without persisting performance state."
            );
            Check.That(
                loaded
                    .GetClipNotes(new(0, 1))
                    .Single()
                    == new LoopNote(72, 100, 2, 0.75),
                "All scene content must roundtrip."
            );
            JsonNode legacy = JsonNode.Parse(json)!;
            legacy["version"] = 2;
            _ = legacy
                .AsObject()
                .Remove("scenes");

            foreach (JsonNode? track in legacy["tracks"]!.AsArray())
            {
                JsonArray clips = track!["clips"]!.AsArray();
                track["notes"] = clips[0]!["notes"]!.DeepClone();
                _ = track
                    .AsObject()
                    .Remove("clips");
            }

            await File.WriteAllTextAsync(path, legacy.ToJsonString());
            SessionSnapshot imported = await SessionStore.LoadAsync(path);
            Check.That(
                imported.Version == 3
                    && imported.Scenes is [{ Id: 0 }]
                    && imported.Tracks[0].Notes.Single().Note == 60,
                "Strict version two must migrate into scene zero."
            );
            legacy["scenes"] = node["scenes"]!.DeepClone();
            await File.WriteAllTextAsync(path, legacy.ToJsonString());
            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));

            foreach (string field in new[] { "clips", "sound" })
            {
                JsonNode nullField = JsonNode.Parse(json)!;
                nullField["tracks"]![0]![field] = null;
                await File.WriteAllTextAsync(path, nullField.ToJsonString());
                await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
            }

            JsonNode malformed = JsonNode.Parse(json)!;
            malformed["tracks"]![0]!["notes"] = new JsonArray();
            await File.WriteAllTextAsync(path, malformed.ToJsonString());
            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
        }

        finally
        {
            File.Delete(path);
        }

        LoopNote[] full =
        [
            .. Enumerable
                .Range(0, 2048)
                .Select(index => new LoopNote(index % 128, 100, index / 128 * 0.25, 0.25))
        ];
        SessionSnapshot snapshot = engine.CaptureSession();
        TrackSnapshot[] tracks =
        [
            .. snapshot.Tracks.Select(
                track => track with
                {
                    Clips = new ClipSnapshot[]
                    {
                        new(0, full),
                        new(1, [])
                    }
                }
            )
        ];
        engine.ApplySession(snapshot with { Tracks = tracks });
        Check.That(
            !engine.TryDuplicateScene(0, "Too much", out _)
                && engine.Scenes.Count == 2
                && !engine.CanUndo,
            "Duplication must preflight the combined project budget atomically."
        );
        tracks[0] = tracks[0] with
        {
            Clips = new ClipSnapshot[]
            {
                new(0, full),
                new(1, [new(80, 100, 0, 0.25)])
            }
        };
        Check.Throws<InvalidDataException>(
            () => engine.ApplySession(snapshot with { Tracks = tracks })
        );
        Check.That(
            engine.GetClipState(new(0, 1)).NoteCount == 0,
            "Rejected global capacity must leave all scenes unchanged."
        );
    }

    public static Task QueuesAndTransport()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        engine.ApplySession(TwoScenes(engine, new(60, 100, 0, 0.25), new(72, 100, 0, 0.25)));
        engine.QueueClipLaunch(new(0, 0));
        Check.That(
            engine.IsPlaying
                && engine.GetTrackPlaybackState(0).ActiveSceneId == 0
                && Enumerable
                    .Range(1, 3)
                    .All(track => engine.GetTrackPlaybackState(track).ActiveSceneId is null),
            "An explicit clip launch from stopped must start only that lane."
        );
        engine.Stop();
        engine.Play();
        _ = Check.Render(engine, 0.25);
        long notes = engine.NoteRevision;
        engine.QueueClipLaunch(new(1, 1));
        _ = Check.Render(engine, 1.749);
        Check.That(
            engine.GetTrackPlaybackState(1).ActiveSceneId == 0,
            "A queued clip cannot switch before the bar."
        );
        _ = Check.Render(engine, 0.002);
        Check.That(
            engine.GetTrackPlaybackState(1).ActiveSceneId == 1
                && engine.GetTrackPlaybackState(0).ActiveSceneId == 0,
            "A clip launch must change only its lane at the bar."
        );
        Check.Near(
            0.002,
            engine.GetTrackPlaybackState(1).LocalBeat,
            0.0001,
            "Launched local phase must begin at zero"
        );
        engine.QueueSceneLaunch(1);
        engine.QueueClipStop(2);
        engine.CancelQueuedLaunch(3);
        _ = Check.Render(engine, 2);
        Check.That(
            engine.GetTrackPlaybackState(0).ActiveSceneId == 1
                && engine.GetTrackPlaybackState(1).ActiveSceneId == 1
                && engine.GetTrackPlaybackState(2).ActiveSceneId is null
                && engine.GetTrackPlaybackState(3).ActiveSceneId == 0,
            "Scene queues must be atomic, individually replaceable, and cancellable."
        );
        Check.That(
            engine.NoteRevision == notes && !engine.CanUndo,
            "Launches must not change content or undo history."
        );
        Span<TrackPlaybackState> states = stackalloc TrackPlaybackState[4];
        engine.CopyTrackPlaybackStates(states);
        Check.That(
            states[2].ActiveSceneId is null,
            "Bulk playback snapshots must describe the actual mix."
        );
        Check.Throws<ArgumentException>(
            () => engine.CopyTrackPlaybackStates(new TrackPlaybackState[3])
        );
        engine.Pause();
        engine.QueueClipLaunch(new(2, 1));
        double paused = engine.BeatPosition;
        _ = Check.Render(engine, 3);
        Check.Near(
            paused,
            engine.BeatPosition,
            0,
            "Paused launch queues must wait for the audio transport"
        );
        engine.Play();
        _ = Check.Render(engine, 2);
        Check.That(
            engine.GetTrackPlaybackState(2).ActiveSceneId == 1,
            "Resume must execute the pending bar launch."
        );
        engine.QueueSceneLaunch(0);
        engine.Stop();
        Check.That(
            engine.GetTrackPlaybackState(0).QueuedSceneId is null,
            "Stop must cancel pending choices."
        );
        engine.EditingSceneId = 1;
        engine.Play();
        Check.That(
            Enumerable
                .Range(0, 4)
                .All(track => engine.GetTrackPlaybackState(track).ActiveSceneId == 1),
            "Play from stopped must launch the editing row."
        );
        engine.LoopEnabled = false;
        _ = Check.Render(engine, 7.75);
        engine.QueueSceneLaunch(0);
        _ = Check.Render(engine, 0.251);
        Check.That(
            !engine.IsPlaying
                && engine.BeatPosition == 16
                && engine.GetTrackPlaybackState(0).QueuedSceneId is null
                    && engine.GetTrackPlaybackState(0).ActiveSceneId == 1,
            "One-pass completion must cancel boundary queues without extending the pass."
        );
        return Task.CompletedTask;
    }

    public static Task RecordingOwnership()
    {
        using WorkstationEngine engine = new()
        {
            Tempo = 120,
            RecordingQuantizationEnabled = false
        };
        engine.ApplySession(TwoScenes(engine, new(120, 100, 15, 0.25)));
        engine.Play();
        engine.EditingSceneId = 1;
        engine.ToggleRecording();
        _ = Check.Render(engine, 0.25);
        engine.NoteOn(0, 60, 100);
        engine.SetSustain(0, true);
        engine.QueueClipLaunch(new(0, 1));
        _ = Check.Render(engine, 1.751);
        LoopNote old = engine
            .GetClipNotes(new(0, 0))
            .Single(note => note.Note == 60);
        Check.Near(
            0.5,
            old.Beat,
            1e-8,
            "Recording must target the active clip instead of the editing row"
        );
        Check.Near(
            3.5,
            old.Duration,
            1e-7,
            "Launch must end the old held recording segment at its boundary"
        );
        engine.NoteOff(0, 60);
        _ = Check.Render(engine, 0.25);
        Check.That(
            Check.Energy(Check.Render(engine, 0.01)) > 1,
            "Launch must retain live sustain ownership."
        );
        engine.SetSustain(0, false);
        LoopNote continuation = engine
            .GetClipNotes(new(0, 1))
            .Single();
        Check.Near(
            0,
            continuation.Beat,
            1e-7,
            "A held recording must continue at the new clip's zero"
        );
        Check.That(
            continuation.Duration > 0.5 && engine.IsRecording,
            "Recording continuation must retain performed gate duration."
        );
        engine.QueueClipStop(0);
        _ = Check.Render(engine, 2);
        engine.NoteOn(0, 62, 100);
        _ = Check.Render(engine, 0.1);
        engine.NoteOff(0, 62);
        Check.That(
            engine.GetClipNotes(new(0, 1)).Count == 1,
            "A stopped lane must not record hidden notes."
        );
        engine.Stop();
        engine.ToggleRecording();
        engine.NoteOn(1, 65, 100);
        _ = Check.Render(engine, 0.1);
        engine.NoteOff(1, 65);
        Check.That(
            engine.GetClipNotes(new(1, 1)).Count == 1,
            "Record from stopped must target the editing row."
        );
        return Task.CompletedTask;
    }

    public static Task EmptyRecordingWorkflow()
    {
        using WorkstationEngine engine = new() { Tempo = 120 };
        engine.Play();
        Check.That(
            engine.TryCreateScene("New idea", out int scene),
            "A new empty scene must be available."
        );
        engine.QueueSceneLaunch(scene);
        _ = Check.Render(engine, 2.001);
        Check.That(
            Enumerable
                .Range(0, 4)
                .All(track => engine.GetTrackPlaybackState(track).ActiveSceneId is null),
            "An unarmed empty row must stop all lanes."
        );
        engine.ToggleRecording();
        Check.That(
            Enumerable
                .Range(0, 4)
                .All(track => engine.GetTrackPlaybackState(track).ActiveSceneId == scene),
            "Record must arm stopped lanes into the editing row."
        );
        engine.NoteOn(3, 36, 100);
        Check.That(
            engine.GetClipNotes(new(3, scene)).Count == 1,
            "Empty-row Record must retain the first hit."
        );
        engine.QueueClipStop(3);
        _ = Check.Render(engine, 2);
        engine.NoteOn(3, 38, 100);
        Check.That(
            engine.GetClipNotes(new(3, scene)).Count == 1,
            "Explicit stop while armed must remain unrecorded."
        );
        engine.QueueClipLaunch(new(3, 0));
        _ = Check.Render(engine, 2);
        engine.NoteOn(3, 40, 100);
        Check.That(
            engine.GetTrackPlaybackState(3).ActiveSceneId == 0
                && engine.GetClipNotes(new(3, 0)).Count == 1,
            "An armed empty launch must become a recording destination."
        );
        return Task.CompletedTask;
    }

    public static Task CaptureAlignment()
    {
        using WorkstationEngine engine = new()
        {
            Tempo = 120,
            RecordingQuantizationEnabled = false
        };
        engine.ApplySession(TwoScenes(engine, new(120, 100, 15, 0.25), new(121, 100, 15, 0.25)));
        engine.Play();
        _ = Check.Render(engine, 0.5);
        engine.QueueClipLaunch(new(0, 1));
        _ = Check.Render(engine, 1.5);
        _ = Check.Render(engine, 0.25);
        engine.NoteOn(0, 60, 100);
        engine.NoteOn(1, 64, 100);
        _ = Check.Render(engine, 0.125);
        engine.NoteOff(0, 60);
        engine.NoteOff(1, 64);
        engine.EditingSceneId = 1;
        Check.That(
            engine.CaptureRecentPerformance().Status == CaptureStatus.Captured,
            "A mixed clip performance must capture."
        );
        Check.Near(
            0.5,
            engine
                .GetClipNotes(new(0, 1))
                .Single(note => note.Note == 60).Beat,
            1e-7,
            "Playing editing clips must use their own phase"
        );
        Check.Near(
            4.5,
            engine
                .GetClipNotes(new(1, 1))
                .Single(note => note.Note == 64).Beat,
            1e-7,
            "Inactive editing clips must retain the common project anchor"
        );
        Check.That(
            engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
            "Repeated capture must remain a no-op."
        );
        _ = Check.Render(engine, (20.49 - engine.BeatPosition) / 2);
        Check.That(
            Check.Energy(Check.Render(engine, 0.005)) == 0,
            "Captured notes must wait for their next local onset without chasing the current pass."
        );
        Check.That(
            Check.Energy(Check.Render(engine, 0.02)) > 1,
            "The captured local onset must actually sound at the offset clip's next pass."
        );
        Check.That(
            engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
            "An idle retained phrase must not drift into phantom edits while playback advances."
        );
        Check.That(
            engine.Undo() && engine.GetClipNotes(new(0, 1)).Count == 1 && engine.Redo(),
            "Scene-aware Capture must be one reversible batch."
        );
        engine.Pause();
        Check.That(
            engine.CaptureRecentPerformance().Status == CaptureStatus.NoChanges,
            "Pausing an aligned mix must not rotate the retained captured phrase."
        );

        using (WorkstationEngine adopted = new()
        {
            Tempo = 120,
            RecordingQuantizationEnabled = false
        })
        {
            adopted.ApplySession(
                TwoScenes(adopted, new(120, 100, 15, 0.25), new(121, 100, 15, 0.25))
            );
            adopted.Play();
            adopted.QueueClipLaunch(new(0, 1));
            _ = Check.Render(adopted, 2.5);
            adopted.NoteOn(0, 60, 100);
            adopted.Pause();
            _ = Check.Render(adopted, 4);
            adopted.Play();
            adopted.NoteOn(1, 64, 100);
            _ = Check.Render(adopted, 0.125);
            adopted.Pause();
            adopted.EditingSceneId = 1;
            Check.That(
                adopted.CaptureRecentPerformance().Status == CaptureStatus.Captured,
                "A gate adopted after a paused epoch must remain capturable."
            );
            Check.Near(
                1,
                adopted
                    .GetClipNotes(new(0, 1))
                    .Single(note => note.Note == 60).Beat,
                1e-7,
                "A paused adopted gate must align against the current shared epoch and clip origin"
            );
        }

        engine.Stop();
        return Task.CompletedTask;
    }

    public static Task AudioAndPartitions()
    {
        using WorkstationEngine whole = new() { Tempo = 120 };
        using WorkstationEngine split = new() { Tempo = 120 };
        whole.ApplySession(TwoScenes(whole, new(60, 100, 0, 8), new(72, 100, 0, 8)));
        split.ApplySession(whole.CaptureSession());

        foreach (WorkstationEngine engine in new[] { whole, split })
        {
            engine.Play();
            engine.NoteOn(2, 67, 100);
            engine.QueueSceneLaunch(1);
        }

        float[] expected = Check.Render(whole, 2.5);
        float[] actual = new float[expected.Length];

        for (int offset = 0; offset < actual.Length;)
        {
            int count = Math.Min(194, actual.Length - offset);
            split.Render(actual, offset, count);
            offset += count;
        }

        Check.That(
            expected.SequenceEqual(actual),
            "Queued launches must be sample-equivalent across callback partitions."
        );

        using (WorkstationEngine live = new())
        using (WorkstationEngine launched = new())
        {
            launched.ApplySession(TwoScenes(launched));
            live.NoteOn(0, 60, 100);
            launched.NoteOn(0, 60, 100);
            launched.Play();
            launched.QueueSceneLaunch(1);
            Check.That(
                Check
                    .Render(live, 2.5)
                    .SequenceEqual(Check.Render(launched, 2.5)),
                "A scene boundary must leave live audio exactly equivalent to an untouched owner."
            );
            live.NoteOff(0, 60);
            launched.NoteOff(0, 60);
            Check.That(
                Check
                    .Render(live, 0.2)
                    .SequenceEqual(Check.Render(launched, 0.2)),
                "Live note-off must retain its owner after the scene boundary."
            );
        }

        using (WorkstationEngine recorded = new() { Tempo = 120 })
        {
            recorded.ToggleRecording();
            recorded.NoteOn(3, 36, 100);
            _ = recorded.EndRecording();
            recorded.QueueClipLaunch(new(3, 0));
            _ = Check.Render(recorded, 1.99);
            Check.That(
                Check.Energy(Check.Render(recorded, 0.02)) > 1,
                "A relaunched clip must discard prior-generation defer flags and play a fresh onset."
            );
        }

        whole.QueueClipStop(0);
        split.QueueClipStop(0);
        _ = Check.Render(whole, 2);
        _ = Check.Render(split, 2);
        Check.That(
            whole.GetTrackPlaybackState(0).ActiveSceneId is null
                && Check.Energy(Check.Render(whole, 0.01)) > 1,
            "Stopping a clip must preserve unrelated sequenced and live voices."
        );
        whole.SetTrackSolo(0, true);
        Check.That(
            Check.Energy(Check.Render(whole, 0.01)) == 0,
            "Solo must monitor only chosen lanes, without altering mute targets."
        );
        whole.SetTrackSolo(2, true);
        Check.That(
            Check.Energy(Check.Render(whole, 0.01)) > 1 && !whole.Tracks[2].IsMuted,
            "Multiple solos must preserve audible live ownership and persistent mute state."
        );
        return Task.CompletedTask;
    }

    private static SessionSnapshot TwoScenes(
        WorkstationEngine engine,
        LoopNote? first = null,
        LoopNote? second = null
    )
    {
        SessionSnapshot source = engine.CaptureSession();
        return source with
        {
            Scenes = new SceneState[]
            {
                new(0, "Verse"),
                new(1, "Chorus")
            },
            Tracks = source.Tracks
                .Select(
                    track => track with
                    {
                        Clips = new ClipSnapshot[]
                        {
                            new(0, first is null ? [] : new[] { first }),
                            new(1, second is null ? [] : new[] { second })
                        }
                    }
                )
                .ToArray()
        };
    }
}
