using System.Globalization;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App.Services;

internal sealed partial class StudioSession
{
    private SessionRowMode sessionRowMode;

    public SceneEditorSnapshot CaptureSceneEditor()
    {
        lock (inputLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int scene = Engine.EditingSceneId;
            long noteRevision = Engine.NoteRevision;
            var notes = new IReadOnlyList<LoopNote>[WorkstationEngine.TrackCount];

            for (int track = 0; track < notes.Length; track++)
            {
                notes[track] = Engine.GetClipNotes(new(track, scene));
            }

            // If the audio clock finalizes a note during these copies, keeping the earlier
            // revision makes the next UI refresh pick up the newer content rather than miss it.
            return new(
                scene,
                Engine.LoopBars,
                noteRevision,
                Engine.Tracks,
                Array.AsReadOnly(notes)
            );
        }
    }

    public void SelectScene(int sceneId)
    {
        lock (inputLock)
        {
            if (CanChangeScene(sceneId))
            {
                SelectSceneCore(sceneId);
            }
        }

        RefreshFeedback();
    }

    public void LaunchScene(int sceneId)
    {
        lock (inputLock)
        {
            if (CanChangeScene(sceneId) && Audio.IsRunning)
            {
                Engine.QueueSceneLaunch(sceneId);
            }
        }

        RefreshFeedback();
    }

    public void LaunchClip(int track, int sceneId)
    {
        lock (inputLock)
        {
            if (IsTrack(track) && CanChangeScene(sceneId) && Audio.IsRunning)
            {
                Engine.QueueClipLaunch(new(track, sceneId));
            }
        }

        RefreshFeedback();
    }

    public void StopClip(int track)
    {
        lock (inputLock)
        {
            if (!disposed && !interactionBlocked && IsTrack(track))
            {
                Engine.QueueClipStop(track);
            }
        }

        RefreshFeedback();
    }

    public void ToggleTrackSolo(int track)
    {
        lock (inputLock)
        {
            if (!disposed && !interactionBlocked && IsTrack(track))
            {
                Engine.SetTrackSolo(track, !Engine.GetTrackSolo(track));
            }
        }

        RefreshFeedback();
    }

    public int? CreateScene(string? name = null)
    {
        int? result = null;

        lock (inputLock)
        {
            if (
                !disposed
                && !interactionBlocked
                && Engine.TryCreateScene(name ?? NextSceneName(), out int id)
            )
            {
                result = id;
                revision++;
            }
        }

        RefreshFeedback();
        return result;
    }

    public int? DuplicateScene(int sourceId, string? name = null)
    {
        int? result = null;

        lock (inputLock)
        {
            if (
                CanChangeScene(sourceId)
                && Engine.TryDuplicateScene(sourceId, name ?? NextSceneName(), out int id)
            )
            {
                result = id;
                revision++;
            }
        }

        RefreshFeedback();
        return result;
    }

    public bool RenameScene(int sceneId, string name)
    {
        bool changed = false;

        lock (inputLock)
        {
            if (CanChangeScene(sceneId))
            {
                SceneState scene = Engine.Scenes.First(scene => scene.Id == sceneId);

                if (scene.Name != name)
                {
                    Engine.RenameScene(sceneId, name);
                    revision++;
                    changed = true;
                }
            }
        }

        RefreshFeedback();
        return changed;
    }

    public bool DeleteScene(int sceneId)
    {
        bool changed = false;

        lock (inputLock)
        {
            if (CanChangeScene(sceneId) && Engine.DeleteScene(sceneId))
            {
                revision++;
                changed = true;
            }
        }

        RefreshFeedback();
        return changed;
    }

    public bool ClearClip(int track, int sceneId)
    {
        bool changed = false;

        lock (inputLock)
        {
            if (IsTrack(track) && CanChangeScene(sceneId) && Engine.ClearClip(new(track, sceneId)))
            {
                revision++;
                changed = true;
            }
        }

        RefreshFeedback();
        return changed;
    }

    private bool CanChangeScene(int sceneId)
    {
        return !disposed && !interactionBlocked && Engine.Scenes.Any(scene => scene.Id == sceneId);
    }

    private void SelectSceneCore(int sceneId)
    {
        if (Engine.EditingSceneId != sceneId)
        {
            Engine.CommitEdit();
            Engine.EditingSceneId = sceneId;
        }
    }

    private void MoveSceneCore(int offset)
    {
        IReadOnlyList<SceneState> scenes = Engine.Scenes;
        int selected = SceneIndex(scenes, Engine.EditingSceneId);
        int next = Math.Clamp(selected + Math.Clamp(offset, -1, 1), 0, scenes.Count - 1);
        SelectSceneCore(scenes[next].Id);
    }

    private void TriggerSessionPadCore(int track, int row)
    {
        if (!IsTrack(track) || row is < 0 or > 1)
        {
            return;
        }

        if (row == 1)
        {
            switch (sessionRowMode)
            {
                case SessionRowMode.Stop:
                    Engine.QueueClipStop(track);
                    return;

                case SessionRowMode.Solo:
                    Engine.SetTrackSolo(track, !Engine.GetTrackSolo(track));
                    return;

                case SessionRowMode.Mute:
                    _ = SetParameterCore(
                        new(ParameterId.TrackMute, track),
                        Engine.Tracks[track].IsMuted ? 0 : 1
                    );
                    return;

                case SessionRowMode.Clips:
                    break;

                default:
                    return;
            }
        }

        if (!Audio.IsRunning)
        {
            return;
        }

        IReadOnlyList<SceneState> scenes = Engine.Scenes;
        int index = SceneIndex(scenes, Engine.EditingSceneId) + row;

        if (index < scenes.Count)
        {
            Engine.QueueClipLaunch(new(track, scenes[index].Id));
        }
    }

    private ControllerSessionFeedback CreateSessionFeedback()
    {
        IReadOnlyList<SceneState> scenes = Engine.Scenes;
        int selected = SceneIndex(scenes, Engine.EditingSceneId);
        Span<TrackPlaybackState> playback = stackalloc TrackPlaybackState[WorkstationEngine.TrackCount];
        Engine.CopyTrackPlaybackStates(playback);
        var top = new ControllerClipFeedback[WorkstationEngine.TrackCount];
        var bottom = new ControllerClipFeedback[WorkstationEngine.TrackCount];
        bool[] solo = new bool[WorkstationEngine.TrackCount];
        bool[] active = new bool[WorkstationEngine.TrackCount];
        bool[] stopping = new bool[WorkstationEngine.TrackCount];

        for (int track = 0; track < top.Length; track++)
        {
            TrackPlaybackState state = playback[track];
            top[track] = ClipFeedback(track, scenes[selected].Id, state);
            bottom[track] = selected + 1 < scenes.Count
                ? ClipFeedback(track, scenes[selected + 1].Id, state)
                : default;
            solo[track] = Engine.GetTrackSolo(track);
            active[track] = (Engine.IsPlaying && state.ActiveSceneId is not null)
                || state.QueuedSceneId is not null;
            stopping[track] = state.StopQueued;
        }

        return new(
            scenes[selected].Name,
            sessionRowMode,
            Array.AsReadOnly(top),
            Array.AsReadOnly(bottom),
            Array.AsReadOnly(solo),
            selected > 0,
            selected + 1 < scenes.Count,
            Array.AsReadOnly(active),
            Array.AsReadOnly(stopping)
        );
    }

    private ControllerClipFeedback ClipFeedback(int track, int scene, TrackPlaybackState playback)
    {
        bool playing = Engine.IsPlaying && playback.ActiveSceneId == scene;
        return new(
            true,
            Engine.GetClipState(new(track, scene)).NoteCount > 0,
            playing,
            playback.QueuedSceneId == scene,
            playing && Engine.IsRecording,
            playback.ActiveSceneId == scene && playback.StopQueued
        );
    }

    private string NextSceneName()
    {
        IReadOnlyList<SceneState> scenes = Engine.Scenes;

        for (int number = 1; number <= WorkstationEngine.MaximumScenes + 1; number++)
        {
            string name = "Scene " + number.ToString(CultureInfo.InvariantCulture);

            if (scenes.All(scene => !StringComparer.OrdinalIgnoreCase.Equals(scene.Name, name)))
            {
                return name;
            }
        }

        throw new InvalidOperationException("No scene name is available.");
    }

    private static int SceneIndex(IReadOnlyList<SceneState> scenes, int sceneId)
    {
        for (int index = 0; index < scenes.Count; index++)
        {
            if (scenes[index].Id == sceneId)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The editing scene is unavailable.");
    }
}
