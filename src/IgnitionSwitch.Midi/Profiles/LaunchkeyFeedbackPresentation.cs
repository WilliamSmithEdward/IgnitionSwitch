using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Pure comparison and LCD presentation for MK3 controller feedback snapshots.</summary>
public static class LaunchkeyFeedbackPresentation
{
    /// <summary>Whether two snapshots would produce the same track and transport feedback.</summary>
    public static bool HasSameState(ControllerFeedback? previous, ControllerFeedback current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return previous is not null
            && previous.SelectedTrack == current.SelectedTrack
            && previous.Playing == current.Playing
            && previous.Recording == current.Recording
            && previous.LoopEnabled == current.LoopEnabled
            && previous.TrackNames.SequenceEqual(current.TrackNames)
            && previous.TrackMuted.SequenceEqual(current.TrackMuted);
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
