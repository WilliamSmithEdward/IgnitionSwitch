using System.Text.Json;
using System.Text.Json.Nodes;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Persistence;
using IgnitionSwitch.Core.Sequencing;

internal static class SchemaTests
{
    public static async Task Migration()
    {
        string path = TemporaryPath();
        string legacy = JsonSerializer.Serialize(new
        {
            version = 1,
            tempo = 120,
            loopBars = 4,
            masterVolume = 0.65f,
            tracks = Enumerable
                .Range(0, 4)
                .Select(index => new
                {
                    name = $"Track {index + 1}",
                    instrument = index,
                    isMuted = false,
                    volume = 0.8f,
                    notes = new[]
                    {
                        new
                        {
                            note = 60,
                            velocity = 100,
                            beat = 0.13,
                            duration = 0.37
                        }
                    }
                })
                .ToArray()
        });

        try
        {
            await File.WriteAllTextAsync(path, legacy);
            SessionSnapshot migrated = await SessionStore.LoadAsync(path);
            Check.That(
                migrated.Version == 3
                    && migrated.Delay is not null
                    && migrated.Reverb is not null
                    && migrated.Tracks.All(track => track.Sound is not null),
                "Version one input must migrate to complete canonical version three settings."
            );
            Check.That(
                migrated.Tracks.All(
                    track => track.Notes.Single() == new LoopNote(60, 100, 0.13, 0.37)
                ),
                "Migration must preserve all legacy musical content."
            );
            using WorkstationEngine engine = new();
            engine.ApplySession(migrated);
            Check.Near(
                0.004,
                engine
                    .GetDescriptor(new(ParameterId.Attack, 0))
                    .ToPhysical(engine.GetParameter(new(ParameterId.Attack, 0))),
                0,
                "Migration must preserve exact native keys attack"
            );
            Check.Near(
                0.08,
                engine
                    .GetDescriptor(new(ParameterId.Attack, 2))
                    .ToPhysical(engine.GetParameter(new(ParameterId.Attack, 2))),
                0,
                "Migration must preserve exact native atmosphere attack"
            );
            Check.That(
                engine.GetParameter(new(ParameterId.Pan, 0)) == 0.5
                    && engine.GetParameter(new(ParameterId.SendA, 0)) == 0
                    && engine.GetParameter(new(ParameterId.SendB, 0)) == 0,
                "Migrated projects must start centered and dry."
            );
            await SessionStore.SaveAsync(path, migrated);
            Check.That(
                JsonNode
                    .Parse(await File.ReadAllTextAsync(path))!["version"]!
                    .GetValue<int>()
                    == 3,
                "Saving migrated input must write version three."
            );

            foreach (string field in new[] { "delay", "reverb" })
            {
                JsonNode invalid = JsonNode.Parse(legacy)!;
                invalid[field] = null;
                await File.WriteAllTextAsync(path, invalid.ToJsonString());
                await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
            }

            JsonNode extendedTrack = JsonNode.Parse(legacy)!;
            extendedTrack["tracks"]![0]!["sound"] = null;
            await File.WriteAllTextAsync(path, extendedTrack.ToJsonString());
            await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));

            foreach (string invalidVersion in new[] { "\"2\"", "null", "{}", "[]", "true" })
            {
                JsonNode invalid = JsonNode.Parse(legacy)!;
                invalid["version"] = JsonNode.Parse(invalidVersion);
                await File.WriteAllTextAsync(path, invalid.ToJsonString());
                await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
            }
        }

        finally
        {
            File.Delete(path);
        }
    }

    public static async Task RoundtripAndValidation()
    {
        using WorkstationEngine engine = new();
        ParameterId[] trackIds = [
            ParameterId.Tone, ParameterId.Attack, ParameterId.Release, ParameterId.Drive,
            ParameterId.Pan, ParameterId.SendA, ParameterId.SendB, ParameterId.Level,
            ParameterId.TrackMute
        ];

        foreach (ParameterId id in Enum.GetValues<ParameterId>())
        {
            if (trackIds.Contains(id))
            {
                for (int track = 0; track < 4; track++)
                {
                    engine.SetParameter(new(id, track), 0.15 + (track * 0.2));
                }
            }

            else
            {
                engine.SetParameter(new(id), 0.73);
            }
        }

        SessionSnapshot source = engine.CaptureSession();
        string path = TemporaryPath();

        try
        {
            await SessionStore.SaveAsync(path, source);
            SessionSnapshot loaded = await SessionStore.LoadAsync(path);
            string completeJson = await File.ReadAllTextAsync(path);

            foreach ((string section, string field) in new[]
            {
                ("delay", "time"),
                ("reverb", "decay"),
                ("sound", "tone")
            })
            {
                JsonNode incomplete = JsonNode.Parse(completeJson)!;
                JsonObject settings = section == "sound"
                    ? incomplete["tracks"]![0]!["sound"]!.AsObject()
                    : incomplete[section]!.AsObject();
                _ = settings.Remove(field);
                await File.WriteAllTextAsync(path, incomplete.ToJsonString());
                await Check.ThrowsAsync<InvalidDataException>(() => SessionStore.LoadAsync(path));
            }

            using WorkstationEngine restored = new();
            restored.ApplySession(loaded);

            foreach (ParameterId id in Enum.GetValues<ParameterId>())
            {
                if (trackIds.Contains(id))
                {
                    for (int track = 0; track < 4; track++)
                    {
                        Check.Near(
                            engine.GetParameter(new(id, track)),
                            restored.GetParameter(new(id, track)),
                            0,
                            "Version two must retain every track target"
                        );
                    }
                }

                else
                {
                    Check.Near(
                        engine.GetParameter(new(id)),
                        restored.GetParameter(new(id)),
                        0,
                        "Version two must retain every global target"
                    );
                }
            }

            Check.That(
                !restored.CanUndo && !restored.CanRedo,
                "Applying sound settings must reset history."
            );

            foreach (SessionSnapshot invalid in new[]
            {
                source with { Delay = null },
                source with { Reverb = null },
                source with { Delay = source.Delay! with { Time = float.NaN } },
                source with { Reverb = source.Reverb! with { Decay = float.PositiveInfinity } },
                source with { Delay = source.Delay! with { Feedback = 1.1f } },
                source with { Version = 1 },
                source with { Version = 99 }
            })
            {
                Check.Throws<InvalidDataException>(() => restored.ApplySession(invalid));
            }

            TrackSnapshot[] tracks = [.. source.Tracks];
            tracks[0] = tracks[0] with { Sound = tracks[0].Sound! with { Pan = -0.1f } };
            Check.Throws<InvalidDataException>(
                () => restored.ApplySession(source with { Tracks = tracks })
            );
            tracks[0] = tracks[0] with { Sound = null };
            Check.Throws<InvalidDataException>(
                () => restored.ApplySession(source with { Tracks = tracks })
            );
            Check.Near(
                engine.GetParameter(new(ParameterId.DelayFeedback)),
                restored.GetParameter(new(ParameterId.DelayFeedback)),
                0,
                "Invalid settings must leave existing state unchanged"
            );
        }

        finally
        {
            File.Delete(path);
        }
    }

    private static string TemporaryPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"IgnitionSwitch-sound-{Guid.NewGuid():N}.ignition.json"
        );
    }
}
