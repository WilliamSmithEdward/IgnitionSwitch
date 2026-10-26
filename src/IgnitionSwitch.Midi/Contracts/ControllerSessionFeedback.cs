using System.Diagnostics.CodeAnalysis;

namespace IgnitionSwitch.Midi.Contracts;

/// <summary>Two visible scene rows and per-track context; all collections match the feedback track count.</summary>
public sealed record ControllerSessionFeedback(
    string SceneName,
    SessionRowMode RowMode,
    IReadOnlyList<ControllerClipFeedback> UpperClips,
    IReadOnlyList<ControllerClipFeedback> LowerClips,
    IReadOnlyList<bool> TrackSolo,
    bool CanMoveUp,
    bool CanMoveDown,
    IReadOnlyList<bool>? TrackActive = null,
    IReadOnlyList<bool>? TrackStopQueued = null
)
{
    /// <summary>Compares visible clips, context, navigation, and normalized per-track activity by content.</summary>
    public static bool HasSameState(
        ControllerSessionFeedback? previous,
        ControllerSessionFeedback? current
    )
    {
        if (previous is null || current is null)
        {
            return previous is null && current is null;
        }

        if (
            !previous.HasMatchingTracks(previous.UpperClips?.Count ?? 0)
            || !current.HasMatchingTracks(current.UpperClips?.Count ?? 0)
            || (previous.SceneName ?? string.Empty) != (current.SceneName ?? string.Empty)
            || previous.RowMode != current.RowMode
            || previous.CanMoveUp != current.CanMoveUp
            || previous.CanMoveDown != current.CanMoveDown
            || !previous.UpperClips.SequenceEqual(current.UpperClips)
            || !previous.LowerClips.SequenceEqual(current.LowerClips)
            || !previous.TrackSolo.SequenceEqual(current.TrackSolo)
        )
        {
            return false;
        }

        for (int track = 0; track < current.UpperClips.Count; track++)
        {
            if (
                previous.IsTrackActive(track) != current.IsTrackActive(track)
                || previous.IsTrackStopQueued(track) != current.IsTrackStopQueued(track)
            )
            {
                return false;
            }
        }

        return true;
    }

    [MemberNotNullWhen(true, nameof(UpperClips), nameof(LowerClips), nameof(TrackSolo))]
    internal bool HasMatchingTracks(int trackCount)
    {
        return trackCount is >= 1
            and <= 8
                && Enum.IsDefined(RowMode)
                && UpperClips is not null
                && LowerClips is not null
                && TrackSolo is not null
                && UpperClips.Count == trackCount
                && LowerClips.Count == trackCount
                && TrackSolo.Count == trackCount
                && (TrackActive is null || TrackActive.Count == trackCount)
                && (TrackStopQueued is null || TrackStopQueued.Count == trackCount);
    }

    internal bool IsTrackActive(int track)
    {
        return TrackActive is { } active
            ? active[track]
            : Active(UpperClips[track]) || Active(LowerClips[track]);
    }

    internal bool IsTrackStopQueued(int track)
    {
        return TrackStopQueued is { } queued
            ? queued[track]
            : (UpperClips[track].Exists && UpperClips[track].StopQueued)
                || (LowerClips[track].Exists && LowerClips[track].StopQueued);
    }

    private static bool Active(ControllerClipFeedback clip)
    {
        return clip.Exists && (clip.Playing || clip.Queued || clip.Recording || clip.StopQueued);
    }
}
