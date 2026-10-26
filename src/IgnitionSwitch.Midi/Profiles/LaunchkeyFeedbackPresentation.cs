using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Pure comparison and LCD presentation for MK3 controller feedback snapshots.</summary>
public static class LaunchkeyFeedbackPresentation
{
    private static readonly ControllerKnobFeedback[] DefaultKnobs =
    [
        new("Tone", ""),
        new("Attack", ""),
        new("Release", ""),
        new("Drive", ""),
        new("Pan", ""),
        new("Echo send", ""),
        new("Room send", ""),
        new("Level", "")
    ];

    /// <summary>Copies caller-owned collections and normalizes all eight knob slots before queuing feedback.</summary>
    public static ControllerFeedback CreateSnapshot(ControllerFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(feedback.TrackNames);
        ArgumentNullException.ThrowIfNull(feedback.TrackMuted);

        if (
            feedback.TrackNames.Count is < 1
                or > 8
                    || feedback.TrackMuted.Count != feedback.TrackNames.Count
                    || feedback.SelectedTrack < 0
                    || feedback.SelectedTrack >= feedback.TrackNames.Count
        )
        {
            throw new ArgumentException(
                "Feedback requires one to eight matching tracks and a valid selection.",
                nameof(feedback)
            );
        }

        string[] names = [.. feedback.TrackNames.Select(name => name ?? string.Empty)];
        bool[] muted = [.. feedback.TrackMuted];
        var knobs = new ControllerKnobFeedback[8];

        for (int index = 0; index < knobs.Length; index++)
        {
            ControllerKnobFeedback source = KnobAt(feedback, index);
            knobs[index] = new(source.Name, source.Value);
        }

        return feedback with
        {
            TrackNames = Array.AsReadOnly(names),
            TrackMuted = Array.AsReadOnly(muted),
            Knobs = Array.AsReadOnly(knobs),
            DeviceName = feedback.DeviceName ?? string.Empty,
            Session = CopySession(feedback.Session, names.Length)
        };
    }

    private static ControllerSessionFeedback? CopySession(
        ControllerSessionFeedback? session,
        int trackCount
    )
    {
        if (session is null)
        {
            return null;
        }

        if (!session.HasMatchingTracks(trackCount))
        {
            throw new ArgumentException(
                "Session feedback collections must match the track count and use a supported row mode.",
                nameof(session)
            );
        }

        ControllerClipFeedback[] upper = [.. session.UpperClips];
        ControllerClipFeedback[] lower = [.. session.LowerClips];
        bool[] solo = [.. session.TrackSolo];
        bool[] active = new bool[trackCount];
        bool[] stopQueued = new bool[trackCount];

        for (int track = 0; track < trackCount; track++)
        {
            active[track] = session.IsTrackActive(track);
            stopQueued[track] = session.IsTrackStopQueued(track);
        }

        return session with
        {
            SceneName = session.SceneName ?? string.Empty,
            UpperClips = Array.AsReadOnly(upper),
            LowerClips = Array.AsReadOnly(lower),
            TrackSolo = Array.AsReadOnly(solo),
            TrackActive = Array.AsReadOnly(active),
            TrackStopQueued = Array.AsReadOnly(stopQueued)
        };
    }

    /// <summary>Gets one of eight normalized knob display slots; omitted feedback uses the initial instrument page.</summary>
    public static ControllerKnobFeedback KnobAt(ControllerFeedback feedback, int knob)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentOutOfRangeException.ThrowIfNegative(knob);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(knob, 7);

        if (feedback.Knobs is null)
        {
            return DefaultKnobs[knob];
        }

        ControllerKnobFeedback? value = knob < feedback.Knobs.Count ? feedback.Knobs[knob] : null;
        return value is not null && value.Name is not null && value.Value is not null
            ? value
            : new(value?.Name ?? string.Empty, value?.Value ?? string.Empty);
    }

    /// <summary>Whether the track selection, recording indication, names, and mute flags match.</summary>
    public static bool HasSameTrackState(ControllerFeedback? previous, ControllerFeedback current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return previous is not null
            && previous.SelectedTrack == current.SelectedTrack
            && previous.Recording == current.Recording
            && previous.TrackNames.SequenceEqual(current.TrackNames)
            && previous.TrackMuted.SequenceEqual(current.TrackMuted);
    }

    /// <summary>Whether two snapshots would produce the same track, transport, device, and knob feedback.</summary>
    public static bool HasSameState(ControllerFeedback? previous, ControllerFeedback current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (
            !HasSameTrackState(previous, current)
            || previous!.Playing != current.Playing
            || previous.LoopEnabled != current.LoopEnabled
            || (previous.DeviceName ?? string.Empty) != (current.DeviceName ?? string.Empty)
            || previous.DeviceLocked != current.DeviceLocked
            || !ControllerSessionFeedback.HasSameState(previous.Session, current.Session)
        )
        {
            return false;
        }

        for (int knob = 0; knob < 8; knob++)
        {
            if (KnobAt(previous, knob) != KnobAt(current, knob))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Formats the LCD transport row, prioritizing recording over the playback loop policy.</summary>
    public static string TransportText(ControllerFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        return feedback.Recording
            ? "Recording"
            : feedback.Playing
                ? feedback.LoopEnabled ? "Looping" : "Playing once"
                : feedback.LoopEnabled ? "Loop ready" : "One pass ready";
    }
}
