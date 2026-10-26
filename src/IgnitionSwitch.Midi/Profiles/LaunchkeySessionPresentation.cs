using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Protocol;

namespace IgnitionSwitch.Midi.Profiles;

/// <summary>Pure Session pad, button, and LCD presentation; the application owns clip and row-mode state.</summary>
public static class LaunchkeySessionPresentation
{
    private static readonly int[] ClipColors = [37, 45, 49, 57, 9, 13, 19, 53];

    /// <summary>Gets row-major Session pad feedback; unused track columns and empty clip slots remain dark.</summary>
    public static LaunchkeyLedFeedback Pad(ControllerFeedback feedback, int pad)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentOutOfRangeException.ThrowIfNegative(pad);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pad, 15);
        int track = pad % 8;

        if (track >= feedback.TrackNames.Count)
        {
            return default;
        }

        if (feedback.Session is not { } session)
        {
            int color = pad < 8
                ? track == feedback.SelectedTrack ? feedback.Recording ? 5 : 45 : 13
                : feedback.TrackMuted[track] ? 5 : 19;
            return new(color);
        }

        return pad < 8
            ? Clip(session.UpperClips[track], track)
            : session.RowMode switch
            {
                SessionRowMode.Clips => Clip(session.LowerClips[track], track),
                SessionRowMode.Stop => session.IsTrackStopQueued(track)
                    ? new(5, LaunchkeyLedMode.Flashing)
                    : new(session.IsTrackActive(track) ? 5 : 7),
                SessionRowMode.Solo => new(session.TrackSolo[track] ? 45 : 47),
                SessionRowMode.Mute => new(feedback.TrackMuted[track] ? 15 : 13),
                _ => default
            };
    }

    /// <summary>Gets Scene Launch, Stop/Solo/Mute, or scene-navigation LED feedback.</summary>
    public static LaunchkeyLedFeedback Button(ControllerFeedback feedback, int controller)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        return feedback.Session is not { } session
            ? default
            : controller switch
            {
                LaunchkeyMk3Protocol.SceneUp => new(session.CanMoveUp ? 3 : 0),
                LaunchkeyMk3Protocol.SceneDown => new(session.CanMoveDown ? 3 : 0),
                LaunchkeyMk3Protocol.StopSoloMute => new(session.RowMode switch
                {
                    SessionRowMode.Stop => 5,
                    SessionRowMode.Solo => 45,
                    SessionRowMode.Mute => 13,
                    SessionRowMode.Clips => 0,
                    _ => 0
                }),
                LaunchkeyMk3Protocol.SceneLaunch => SceneLaunch(session),
                _ => throw new ArgumentOutOfRangeException(nameof(controller))
            };
    }

    /// <summary>Formats the default title for the active hardware layout without changing its mode.</summary>
    public static string DisplayTitle(ControllerFeedback feedback, int padMode)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        return padMode == 9 && !string.IsNullOrEmpty(feedback.DeviceName)
            ? feedback.DeviceName
            : padMode == 2 && feedback.Session is { } session
                ? string.IsNullOrEmpty(session.SceneName) ? "Scene" : session.SceneName
                : feedback.TrackNames[feedback.SelectedTrack];
    }

    /// <summary>Includes the app's lower-row role and transport context, prioritizing recording.</summary>
    public static string DisplayDetail(ControllerFeedback feedback, int padMode)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        if (feedback.Recording || padMode != 2 || feedback.Session is not { } session)
        {
            return LaunchkeyFeedbackPresentation.TransportText(feedback);
        }

        string transport = feedback.Playing
            ? feedback.LoopEnabled ? "Looping" : "Play once"
            : feedback.LoopEnabled ? "Loop ready" : "Once ready";
        return $"{session.RowMode} {transport}";
    }

    private static LaunchkeyLedFeedback Clip(ControllerClipFeedback clip, int track)
    {
        return clip switch
        {
            { Exists: false } => default,
            { Recording: true } => new(5, LaunchkeyLedMode.Pulsing),
            { StopQueued: true } => new(5, LaunchkeyLedMode.Flashing),
            { Queued: true } => new(19, LaunchkeyLedMode.Flashing),
            { HasNotes: false } => default,
            { Playing: true } => new(19, LaunchkeyLedMode.Pulsing),
            _ => new(ClipColors[track])
        };
    }

    private static LaunchkeyLedFeedback SceneLaunch(ControllerSessionFeedback session)
    {
        return session.UpperClips.Any(clip => clip.Exists && clip.Recording)
            ? new(5, LaunchkeyLedMode.Pulsing)
            : session.UpperClips.Any(clip => clip.Exists && clip.StopQueued)
                ? new(5, LaunchkeyLedMode.Flashing)
                : session.UpperClips.Any(clip => clip.Exists && clip.Queued)
                    ? new(19, LaunchkeyLedMode.Flashing)
                    : session.UpperClips.Any(clip => clip.Exists && clip.HasNotes && clip.Playing)
                        ? new(19, LaunchkeyLedMode.Pulsing)
                        : new(session.UpperClips.Any(clip => clip.Exists && clip.HasNotes) ? 3 : 0);
    }
}
