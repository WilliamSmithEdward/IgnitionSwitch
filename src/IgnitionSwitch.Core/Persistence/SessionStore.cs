using System.Text.Json;
using System.Text.Json.Serialization;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

namespace IgnitionSwitch.Core.Persistence;

/// <summary>Reads and atomically saves bounded, validated JSON project files.</summary>
public static class SessionStore
{
    /// <summary>The largest accepted project file, four mebibytes.</summary>
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 16
    };

    /// <summary>Validates a project and replaces its file after a complete write; cancellation preserves the old file.</summary>
    public static async Task SaveAsync(
        string path,
        SessionSnapshot session,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SessionSnapshot validated = ValidateSession(session);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(
            ToDocument(validated),
            SerializerOptions
        );

        if (content.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("This project exceeds the 4 MiB project limit.");
        }

        string target = Path.GetFullPath(path);
        string temporary = target
            + "."
            + Guid
                .NewGuid()
                .ToString("N")
            + ".tmp";

        try
        {
            await using (FileStream file = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough
            ))
            {
                await file
                    .WriteAsync(content, cancellationToken)
                    .ConfigureAwait(false);
                await file
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                file.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
        }

        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Reads a bounded JSON file, validates the complete schema, and returns detached read-only content.</summary>
    public static async Task<SessionSnapshot> LoadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream file = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        if (file.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("This project exceeds the 4 MiB project limit.");
        }

        SessionSnapshot? session;

        try
        {
            using JsonDocument document = await JsonDocument
                .ParseAsync(file, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken)
                .ConfigureAwait(false);
            JsonElement root = document.RootElement;

            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int schema)
            )
            {
                throw new InvalidDataException("This project has no valid schema version.");
            }

            session = schema switch
            {
                1 => ImportLegacy(root.Deserialize<LegacySessionDocument>(SerializerOptions)),
                2 => ImportVersionTwo(
                    root.Deserialize<VersionTwoSessionDocument>(SerializerOptions)
                ),
                3 => ImportScenes(root.Deserialize<SceneSessionDocument>(SerializerOptions)),
                _ => throw new InvalidDataException("Unsupported project version.")
            };
        }

        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "This file is not a valid IgnitionSwitch project.",
                exception
            );
        }

        return ValidateSession(session);
    }

    internal static SessionSnapshot ValidateSession(SessionSnapshot? session)
    {
        if (
            session is null
            || session.Version is not (1 or 2 or 3)
            || session.LoopBars != 4
            || !InRange(session.Tempo, 60, 180)
            || !InRange(session.MasterVolume, 0, 1)
            || session.Tracks is null
            || session.Tracks.Count != WorkstationEngine.TrackCount
        )
        {
            throw new InvalidDataException(
                "Unsupported project version, loop size, tempo, volume, or track count."
            );
        }

        if (
            session.Version == 1
            && (session.Delay is not null
                || session.Reverb is not null
                || session.Tracks.Any(track => track?.Sound is not null))
        )
        {
            throw new InvalidDataException(
                "Version one projects cannot contain version two sound settings."
            );
        }

        if (session.Version >= 2 && (session.Delay is null || session.Reverb is null))
        {
            throw new InvalidDataException("Version two projects require effect settings.");
        }

        DelaySettings delay = session.Delay ?? ParameterCatalog.DelayDefaults;
        ReverbSettings reverb = session.Reverb ?? ParameterCatalog.ReverbDefaults;
        ValidateNormalized(
            [
                delay.Time, delay.Feedback, delay.Damping, delay.Return, reverb.Decay,
                reverb.Damping, reverb.PreDelay, reverb.Return
            ]
        );

        IReadOnlyList<SceneState> scenes = session.Scenes ?? [new(0, "Scene 1")];

        if (
            (session.Version == 3 && session.Scenes is null)
            || (session.Version < 3 && session.Scenes is not null)
            || scenes.Count is < 1
                or > WorkstationEngine.MaximumScenes
                    || scenes
                        .Select(scene => scene?.Id)
                        .Distinct()
                        .Count()
                        != scenes.Count
                    || scenes.Any(
                        scene => scene is null
                            || scene.Id is < 0
                            or >= WorkstationEngine.MaximumScenes || !ValidName(scene.Name)
                    )
        )
        {
            throw new InvalidDataException("The project's scene list is invalid.");
        }

        TrackSnapshot[] tracks = [.. session.Tracks];
        int totalNotes = 0;

        for (int index = 0; index < tracks.Length; index++)
        {
            TrackSnapshot track = tracks[index];

            if (
                track is null
                || !ValidName(track.Name)
                || !Enum.IsDefined(track.Instrument)
                || !InRange(track.Volume, 0, 1)
                || track.Clips is null
                || (session.Version >= 2 && track.Sound is null)
                || track.Clips.Count != scenes.Count
                || track.Clips
                    .Select(clip => clip?.SceneId)
                    .Distinct()
                    .Count()
                    != scenes.Count
            )
            {
                throw new InvalidDataException(
                    "A project track has invalid metadata or clip ownership."
                );
            }

            var clips = new ClipSnapshot[scenes.Count];

            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                ClipSnapshot clip = track.Clips[clipIndex];

                if (
                    clip is null
                    || !scenes.Any(scene => scene.Id == clip.SceneId)
                    || clip.Notes is null
                    || clip.Notes.Count > WorkstationEngine.MaximumNotesPerTrack
                )
                {
                    throw new InvalidDataException(
                        "A project clip has invalid ownership or note count."
                    );
                }

                LoopNote[] notes = [.. clip.Notes];
                totalNotes += notes.Length;

                foreach (LoopNote note in notes)
                {
                    if (
                        note is null
                            || note.Note is < 0
                            or > 127 || note.Velocity is < 1
                            or > 127
                                || !InRange(note.Beat, 0, 16)
                                || note.Beat >= 16
                                || !InRange(note.Duration, double.Epsilon, 16)
                    )
                    {
                        throw new InvalidDataException(
                            "A project note is outside the supported MIDI or loop range."
                        );
                    }
                }

                clips[clipIndex] = new(clip.SceneId, Array.AsReadOnly(notes));
            }

            TrackSoundSettings sound = track.Sound
                ?? ParameterCatalog.TrackDefaults(track.Instrument);
            ValidateNormalized(
                [
                    sound.Tone, sound.Attack, sound.Release, sound.Drive, sound.Pan, sound.SendA,
                    sound.SendB
                ]
            );
            tracks[index] = track with
            {
                Clips = Array.AsReadOnly(clips),
                Sound = sound
            };
        }

        return totalNotes > WorkstationEngine.MaximumTotalNotes
            ? throw new InvalidDataException("The project exceeds its total note budget.")
            : (session with
            {
                Version = 3,
                Tracks = Array.AsReadOnly(tracks),
                Delay = delay,
                Reverb = reverb,
                Scenes = Array.AsReadOnly(scenes.ToArray())
            });
    }

    private static bool ValidName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 32 && !name.Any(char.IsControl);
    }

    private static SceneSessionDocument ToDocument(SessionSnapshot session)
    {
        return new(
            3,
            session.Tempo,
            session.LoopBars,
            session.MasterVolume,
            Array.AsReadOnly(
                session.Tracks
                    .Select(
                        track => new SceneTrackDocument(
                            track.Name,
                            track.Instrument,
                            track.IsMuted,
                            track.Volume,
                            track.Clips,
                            track.Sound!
                        )
                    )
                    .ToArray()
            ),
            session.Delay!,
            session.Reverb!,
            session.Scenes!
        );
    }

    private static SessionSnapshot? ImportScenes(SceneSessionDocument? document)
    {
        return document is null
            ? null
            : new(
                document.Version,
                document.Tempo,
                document.LoopBars,
                document.MasterVolume,
                document.Tracks
                    ?.Select(
                        track => track is null
                            ? null!
                            : new TrackSnapshot(
                                track.Name,
                                track.Instrument,
                                track.IsMuted,
                                track.Volume,
                                [],
                                track.Sound,
                                track.Clips
                            )
                    )
                    .ToArray()!,
                document.Delay,
                document.Reverb,
                document.Scenes
            );
    }

    private static SessionSnapshot? ImportVersionTwo(VersionTwoSessionDocument? document)
    {
        return document is null
            ? null
            : new(
                document.Version,
                document.Tempo,
                document.LoopBars,
                document.MasterVolume,
                document.Tracks
                    ?.Select(
                        track => track is null
                            ? null!
                            : new TrackSnapshot(
                                track.Name,
                                track.Instrument,
                                track.IsMuted,
                                track.Volume,
                                track.Notes,
                                track.Sound
                            )
                    )
                    .ToArray()!,
                document.Delay,
                document.Reverb
            );
    }

    private static SessionSnapshot? ImportLegacy(LegacySessionDocument? document)
    {
        if (document is null)
        {
            return null;
        }

        TrackSnapshot[]? tracks = document.Tracks
            ?.Select(
                track => track is null
                    ? null!
                    : new TrackSnapshot(
                        track.Name,
                        track.Instrument,
                        track.IsMuted,
                        track.Volume,
                        track.Notes!
                    )
            )
            .ToArray();
        return new(
            document.Version,
            document.Tempo,
            document.LoopBars,
            document.MasterVolume,
            tracks!
        );
    }

    private static void ValidateNormalized(ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value) || value is < 0 or > 1)
            {
                throw new InvalidDataException(
                    "A sound parameter is outside its normalized range."
                );
            }
        }
    }

    private static bool InRange(double value, double minimum, double maximum)
    {
        return double.IsFinite(value) && value >= minimum && value <= maximum;
    }
}
