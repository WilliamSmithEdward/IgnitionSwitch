namespace IgnitionSwitch.Core.Domain;

/// <summary>The outcome of an atomic retrospective performance capture.</summary>
public enum CaptureStatus
{
    /// <summary>Recent performance was merged as one project edit.</summary>
    Captured,
    /// <summary>The candidate notes already match project content.</summary>
    NoChanges,
    /// <summary>No eligible live performance is available.</summary>
    NoPerformance,
    /// <summary>The complete merge would exceed a track's note limit.</summary>
    CapacityExceeded,
    /// <summary>Bounded history has lost notes needed by the current capture window.</summary>
    HistoryTruncated
}
