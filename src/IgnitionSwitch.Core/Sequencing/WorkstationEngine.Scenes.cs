using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing.History;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly List<SceneState> _scenes = [new(0, "Scene 1")];
    private int _editingSceneId;
    private long _launchRevision;
    private bool _resumeExistingMix;

    /// <summary>Gets or sets the scene targeted by editing and retrospective Capture, independently of playing clips.</summary>
    public int EditingSceneId
    {
        get
        {
            lock (_sync)
            {
                return _editingSceneId;
            }
        }
        set
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _ = ValidateScene(value);
                SetEditingScene(value);
            }
        }
    }

    /// <summary>Gets detached scene metadata in display order.</summary>
    public IReadOnlyList<SceneState> Scenes
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(_scenes.ToArray());
            }
        }
    }

    /// <summary>Gets the revision of transient clip choices, queued actions, and solo flags.</summary>
    public long LaunchRevision
    {
        get
        {
            lock (_sync)
            {
                return _launchRevision;
            }
        }
    }

    /// <summary>Gets a detached summary of one existing clip.</summary>
    public ClipState GetClipState(ClipAddress address)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateAddress(address);
            return new(address, _tracks[address.Track].Clips[address.SceneId].Notes.Count);
        }
    }

    /// <summary>Gets detached, read-only notes without copying other clips or project state.</summary>
    public IReadOnlyList<LoopNote> GetClipNotes(ClipAddress address)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateAddress(address);
            return Array.AsReadOnly(
                _tracks[address.Track].Clips[address.SceneId].Notes
                    .Select(note => note.Note)
                    .ToArray()
            );
        }
    }

    /// <summary>Gets one lane's active and queued clip choices and local position.</summary>
    public TrackPlaybackState GetTrackPlaybackState(int track)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
            return PlaybackState(_tracks[track]);
        }
    }

    /// <summary>Copies four playback states under one lock without allocating. The destination must have at least four entries.</summary>
    public void CopyTrackPlaybackStates(Span<TrackPlaybackState> destination)
    {
        if (destination.Length < TrackCount)
        {
            throw new ArgumentException(
                "Four playback state entries are required.",
                nameof(destination)
            );
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            for (int track = 0; track < TrackCount; track++)
            {
                destination[track] = PlaybackState(_tracks[track]);
            }
        }
    }

    /// <summary>Creates and selects an empty scene as one undoable edit. Returns false at the scene limit.</summary>
    public bool TryCreateScene(string name, out int sceneId)
    {
        ValidateSceneName(name);

        lock (_sync)
        {
            ThrowIfDisposed();
            sceneId = FindFreeScene();

            if (sceneId < 0)
            {
                return false;
            }

            SceneEdit edit = new(sceneId, _scenes.Count, null, new(sceneId, name), []);
            CommitSceneEdit(edit);
            SetEditingScene(sceneId);
            return true;
        }
    }

    /// <summary>Duplicates a scene's four clips as one undoable edit, atomically checking scene and total-note capacity.</summary>
    public bool TryDuplicateScene(int sourceId, string name, out int sceneId)
    {
        ValidateSceneName(name);

        lock (_sync)
        {
            ThrowIfDisposed();
            _ = ValidateScene(sourceId);
            sceneId = FindFreeScene();
            int count = _tracks.Sum(track => track.Clips[sourceId].Notes.Count);

            if (sceneId < 0 || TotalNoteCount() + count > MaximumProjectNotes)
            {
                sceneId = -1;
                return false;
            }

            List<SceneNote> notes = new(count);

            for (int track = 0; track < TrackCount; track++)
            {
                foreach (StoredNote note in _tracks[track].Clips[sourceId].Notes)
                {
                    notes.Add(new(track, NextVoiceId(), note.Note));
                }
            }

            CommitSceneEdit(new(sceneId, _scenes.Count, null, new(sceneId, name), notes));
            SetEditingScene(sceneId);
            return true;
        }
    }

    /// <summary>Renames an existing scene as one undoable edit; an unchanged name is a no-op.</summary>
    public void RenameScene(int sceneId, string name)
    {
        ValidateSceneName(name);

        lock (_sync)
        {
            ThrowIfDisposed();
            int index = ValidateScene(sceneId);
            SceneState before = _scenes[index];

            if (before.Name != name)
            {
                CommitSceneEdit(new(sceneId, index, before, new(sceneId, name), []));
            }
        }
    }

    /// <summary>Deletes an existing scene and stops only its sequenced clips. The final scene cannot be deleted.</summary>
    public bool DeleteScene(int sceneId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            int index = ValidateScene(sceneId);

            if (_scenes.Count == 1)
            {
                return false;
            }

            for (int track = 0; track < TrackCount; track++)
            {
                if (_tracks[track].ActiveSceneId == sceneId)
                {
                    TransitionClip(track, null, _absoluteBeat);
                }
            }

            _history.Commit();
            List<SceneNote> notes = [];

            for (int track = 0; track < TrackCount; track++)
            {
                foreach (StoredNote note in _tracks[track].Clips[sceneId].Notes)
                {
                    notes.Add(new(track, note.Id, note.Note));
                }
            }

            CommitSceneEdit(new(sceneId, index, _scenes[index], null, notes));
            return true;
        }
    }

    /// <summary>Launches one clip at the next bar, or immediately when stopped. A paused launch waits for resumed playback.</summary>
    /// <remarks>A stopped launch starts only this lane. An empty clip stops the lane unless recording is active, when it becomes a recording destination.</remarks>
    public void QueueClipLaunch(ClipAddress address)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ValidateAddress(address);
            QueueClipAction(address.Track, address.SceneId);
        }
    }

    /// <summary>Atomically queues all four clips of a scene for the same next-bar boundary.</summary>
    /// <remarks>Each launched clip restarts at local zero. Empty clips stop their lanes unless recording is active.</remarks>
    public void QueueSceneLaunch(int sceneId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _ = ValidateScene(sceneId);
            bool stopped = !_isPlaying && !_resumeExistingMix;

            if (stopped)
            {
                StartPlayback(false);
            }

            for (int track = 0; track < TrackCount; track++)
            {
                SetClipAction(track, sceneId, stopped);
            }
        }
    }

    /// <summary>Stops one sequenced lane at the next bar, preserving its live notes and shared effect tails.</summary>
    public void QueueClipStop(int track)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_isPlaying && !_resumeExistingMix)
            {
                TransitionClip(track, null, _absoluteBeat);
            }

            else
            {
                SetClipAction(track, null, false);
            }
        }
    }

    /// <summary>Cancels one pending launch or stop without changing active playback.</summary>
    public void CancelQueuedLaunch(int track)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_tracks[track].HasQueue)
            {
                _tracks[track].HasQueue = false;
                _launchRevision++;
            }
        }
    }

    /// <summary>Gets a transient track solo flag.</summary>
    public bool GetTrackSolo(int track)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();
            return _tracks[track].IsSolo;
        }
    }

    /// <summary>Sets a transient monitoring solo flag without changing persistent mute or content history.</summary>
    /// <remarks>Solo suppresses other dry lanes and new effect feeds. Previously fed shared effect tails may finish.</remarks>
    public void SetTrackSolo(int track, bool solo)
    {
        ValidateTrack(track);

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_tracks[track].IsSolo != solo)
            {
                _tracks[track].IsSolo = solo;
                _launchRevision++;
            }
        }
    }

    private void QueueClipAction(int track, int? sceneId)
    {
        bool stopped = !_isPlaying && !_resumeExistingMix;

        if (stopped)
        {
            StartPlayback(false);
        }

        SetClipAction(track, sceneId, stopped);
    }

    private void SetClipAction(int index, int? sceneId, bool immediate)
    {
        if (immediate)
        {
            TransitionClip(index, sceneId, _absoluteBeat);
            return;
        }

        Track track = _tracks[index];
        track.HasQueue = true;
        track.QueuedSceneId = sceneId;
        double bar = _absoluteBeat / 4;
        double nearest = Math.Round(bar);

        if (Math.Abs(bar - nearest) < BeatBoundaryTolerance / 4)
        {
            bar = nearest;
        }

        track.QueueBeat = (Math.Floor(bar) + 1) * 4;
        _launchRevision++;
    }

    private readonly record struct ClipAction(int Track, int? SceneId, double Origin);

    private void ApplyDueLaunches()
    {
        Span<ClipAction> actions = stackalloc ClipAction[TrackCount];
        int count = 0;

        for (int track = 0; track < TrackCount; track++)
        {
            Track lane = _tracks[track];

            if (lane.HasQueue && _absoluteBeat >= lane.QueueBeat - BeatBoundaryTolerance)
            {
                actions[count++] = new(track, lane.QueuedSceneId, lane.QueueBeat);
            }
        }

        if (count > 0)
        {
            ApplyClipActions(actions[..count]);
        }
    }

    private void TransitionClip(int track, int? sceneId, double origin)
    {
        Span<ClipAction> actions = [new(track, sceneId, origin)];
        ApplyClipActions(actions);
    }

    private void ApplyClipActions(ReadOnlySpan<ClipAction> actions)
    {
        long revision = _noteRevision;
        _history.Commit();

        foreach (ClipAction action in actions)
        {
            Track track = _tracks[action.Track];
            int? sceneId = action.SceneId;

            if (
                !_isRecording
                && sceneId is int emptyScene
                && track.Clips[emptyScene].Notes.Count == 0
            )
            {
                sceneId = null;
            }

            foreach (HeldNote held in _held.Values)
            {
                if (held.Track != action.Track)
                {
                    continue;
                }

                bool continueRecording = held.RecordStart is not null;

                if (held.RecordStart is double start)
                {
                    RecordNote(held, _absoluteBeat - start, false);
                }

                held.RecordStart = continueRecording && _isRecording && sceneId is not null
                    ? _absoluteBeat
                    : null;
                held.RecordSceneId = sceneId ?? _editingSceneId;
                held.RecordOrigin = action.Origin;
            }

            if (track.ActiveSceneId is not null)
            {
                foreach (SynthVoice voice in _voices)
                {
                    if (voice.IsLoop && voice.Track == action.Track)
                    {
                        voice.BeginClipFade();
                    }
                }
            }

            track.ActiveSceneId = sceneId;
            track.Origin = action.Origin;
            track.Cycle = -1;
            track.Generation++;
            track.HasQueue = false;

            if (sceneId is int current)
            {
                track.Clips[current].Cursor = 0;
            }

            _launchRevision++;
        }

        _history.Commit();

        if (revision != _noteRevision)
        {
            RebuildSchedule();
            AlignHistorySchedule();
        }
    }

    private void ArmStoppedRecordingLanes()
    {
        Span<ClipAction> actions = stackalloc ClipAction[TrackCount];
        int count = 0;

        for (int track = 0; track < TrackCount; track++)
        {
            if (_tracks[track].ActiveSceneId is null)
            {
                actions[count++] = new(track, _editingSceneId, _absoluteBeat);
            }
        }

        if (count > 0)
        {
            ApplyClipActions(actions[..count]);
        }
    }

    private void RenderScheduledFrame()
    {
        for (int index = 0; index < TrackCount; index++)
        {
            Track track = _tracks[index];

            if (track.ActiveSceneId is not int sceneId)
            {
                continue;
            }

            Clip clip = track.Clips[sceneId];
            double elapsed = Math.Max(0, _absoluteBeat - track.Origin);
            long cycle = (long)Math.Floor((elapsed + BeatBoundaryTolerance) / LoopBeats);
            double position = elapsed - (cycle * LoopBeats);

            if (cycle != track.Cycle)
            {
                track.Cycle = cycle;
                clip.Cursor = 0;
            }

            while (
                clip.Cursor < clip.Schedule.Count
                && clip.Schedule[clip.Cursor].Beat <= position + BeatBoundaryTolerance
            )
            {
                TriggerScheduled(clip.Schedule[clip.Cursor++]);
            }
        }
    }

    private void PrepareEditingRow()
    {
        Span<ClipAction> actions = stackalloc ClipAction[TrackCount];

        for (int track = 0; track < TrackCount; track++)
        {
            actions[track] = new(track, _editingSceneId, _absoluteBeat);
        }

        ApplyClipActions(actions);
    }

    private void ClearLaunchQueues()
    {
        bool changed = false;

        foreach (Track track in _tracks)
        {
            changed |= track.HasQueue;
            track.HasQueue = false;
        }

        if (changed)
        {
            _launchRevision++;
        }
    }

    private TrackPlaybackState PlaybackState(Track track)
    {
        double elapsed = Math.Max(0, _absoluteBeat - track.Origin);
        double beat = track.ActiveSceneId is null
            ? 0
            : _passCompleted && Math.Abs(elapsed % LoopBeats) < BeatBoundaryTolerance
                ? LoopBeats
                : WrapClipBeat(elapsed);
        return new(
            track.ActiveSceneId,
            track.HasQueue ? track.QueuedSceneId : null,
            track.HasQueue && track.QueuedSceneId is null,
            beat
        );
    }

    private int TotalNoteCount()
    {
        int count = 0;

        foreach (Track track in _tracks)
        {
            foreach (SceneState scene in _scenes)
            {
                count += track.Clips[scene.Id].Notes.Count;
            }
        }

        return count;
    }

    private int FindFreeScene()
    {
        for (int id = 0; id < MaximumScenes; id++)
        {
            if (!_scenes.Any(scene => scene.Id == id))
            {
                return id;
            }
        }

        return -1;
    }

    private int ValidateScene(int sceneId)
    {
        int index = _scenes.FindIndex(scene => scene.Id == sceneId);
        return index < 0 ? throw new ArgumentOutOfRangeException(nameof(sceneId)) : index;
    }

    private void ValidateAddress(ClipAddress address)
    {
        ValidateTrack(address.Track);
        _ = ValidateScene(address.SceneId);
    }

    private static void ValidateSceneName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32 || name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Scene names must contain 1 to 32 printable characters.",
                nameof(name)
            );
        }
    }

    private void SetEditingScene(int sceneId)
    {
        if (_editingSceneId == sceneId)
        {
            return;
        }

        _editingSceneId = sceneId;

        foreach (Track track in _tracks)
        {
            track.EditSceneId = sceneId;
        }

        _launchRevision++;
    }

    private void RemoveClipVoices(ClipAddress address)
    {
        if (_tracks[address.Track].ActiveSceneId != address.SceneId)
        {
            return;
        }

        for (int voice = _voices.Count - 1; voice >= 0; voice--)
        {
            if (_voices[voice].IsLoop && _voices[voice].Track == address.Track)
            {
                _voices.RemoveAt(voice);
            }
        }
    }

    private void CommitSceneEdit(SceneEdit edit)
    {
        _history.Commit();
        _history.RecordScene(edit);
        ApplySceneEdit(edit, false);
    }

    private void ApplySceneEdit(SceneEdit edit, bool reverse)
    {
        SceneState? target = reverse ? edit.Before : edit.After;
        int existing = _scenes.FindIndex(scene => scene.Id == edit.SceneId);

        if (edit.Before is not null && edit.After is not null)
        {
            _scenes[existing] = target!;
        }

        else if (target is null)
        {
            for (int track = 0; track < TrackCount; track++)
            {
                Track lane = _tracks[track];

                if (lane.ActiveSceneId == edit.SceneId)
                {
                    TransitionClip(track, null, _absoluteBeat);
                }

                if (lane.HasQueue && lane.QueuedSceneId == edit.SceneId)
                {
                    lane.HasQueue = false;
                }

                foreach (StoredNote note in lane.Clips[edit.SceneId].Notes)
                {
                    _ = _pendingLoopStarts.Remove(note.Id);
                }

                lane.Clips[edit.SceneId].Notes.Clear();
            }

            _scenes.RemoveAt(existing);

            if (_editingSceneId == edit.SceneId)
            {
                SetEditingScene(_scenes[0].Id);
            }
        }

        else
        {
            _scenes.Insert(Math.Min(edit.Index, _scenes.Count), target);

            foreach (SceneNote note in edit.Notes)
            {
                _tracks[note.Track].Clips[edit.SceneId].Notes.Add(
                    new(note.Id, note.Note, edit.SceneId)
                );
            }
        }

        RebuildSchedule();
        AlignHistorySchedule();
        _noteRevision++;
        _launchRevision++;
    }
}
