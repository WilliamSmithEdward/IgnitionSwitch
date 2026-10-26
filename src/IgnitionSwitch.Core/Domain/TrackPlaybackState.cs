namespace IgnitionSwitch.Core.Domain;

/// <summary>A detached summary of one track's transient clip playback.</summary>
/// <param name="ActiveSceneId">The active clip's scene, or null for a stopped lane.</param>
/// <param name="QueuedSceneId">The queued clip's scene, or null.</param>
/// <param name="StopQueued">Whether a lane stop is queued.</param>
/// <param name="LocalBeat">The active clip's local beat position, retaining sixteen at an aligned completed pass.</param>
public readonly record struct TrackPlaybackState(
    int? ActiveSceneId,
    int? QueuedSceneId,
    bool StopQueued,
    double LocalBeat
);
