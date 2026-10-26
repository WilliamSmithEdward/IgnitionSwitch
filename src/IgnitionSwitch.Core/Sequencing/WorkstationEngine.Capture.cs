using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing.History;
using IgnitionSwitch.Core.Sequencing.Performance;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly PerformanceHistory _performance = new(TrackCount);
    private CaptureWorkspace? _captureWorkspace;

    /// <summary>Gets whether the current retrospective phrase contains live performance, including held gates.</summary>
    public bool HasRecentPerformance
    {
        get
        {
            lock (_sync)
            {
                return _performance.HasRecentPerformance;
            }
        }
    }

    /// <summary>Atomically merges the latest four-bar live phrase across all tracks into the editing scene as one undoable edit, preserving transport, recording, and live ownership.</summary>
    /// <remarks>Retrospective state is bounded and transient. Onsets use their note-on grid policy; gate durations preserve performed timing. Capacity or relevant history loss leaves project content unchanged. Playing destinations align to their local clip origins; inactive destinations retain the common phrase anchor.</remarks>
    public CaptureResult CaptureRecentPerformance()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_performance.HasRecentPerformance)
            {
                return new(CaptureStatus.NoPerformance, 0);
            }

            CaptureWorkspace workspace = _captureWorkspace ??= new(TrackCount, MaximumProjectNotes);

            try
            {
                return CapturePerformance(workspace);
            }

            finally
            {
                workspace.Clear();
            }
        }
    }

    private CaptureResult CapturePerformance(CaptureWorkspace workspace)
    {
        List<PerformanceNote> source = workspace.Source;
        PerformanceWindow window = _performance.CopyWindow(source);

        if (window.Truncated)
        {
            return new(CaptureStatus.HistoryTruncated, 0);
        }

        Dictionary<CaptureKey, CaptureCandidate> candidates = workspace.Candidates;

        foreach (PerformanceNote played in source)
        {
            double start = Math.Max(played.Start, window.Start);
            double end = Math.Min(played.End, window.End);

            if (end <= start)
            {
                continue;
            }

            double offset = window.ProjectAligned ? window.ProjectOffset : window.Start;
            Track lane = _tracks[played.Track];

            if (window.ProjectAligned && lane.ActiveSceneId == _editingSceneId)
            {
                offset = window.ProjectOffset + lane.Origin;
            }

            double onset = (start - offset) % LoopBeats;
            onset = WrapClipBeat(onset);
            onset = played.Quantize ? QuantizeOnset(onset) : onset;
            double duration = Math.Clamp(end - start, played.MinimumDuration, LoopBeats);
            CaptureKey key = new(played.Track, played.Note, onset);

            if (
                !candidates.TryGetValue(key, out CaptureCandidate previous)
                || played.Ordinal > previous.Ordinal
            )
            {
                candidates[key] = new(
                    played.Track,
                    played.Ordinal,
                    played.Note,
                    played.Velocity,
                    onset,
                    duration
                );
            }
        }

        if (candidates.Count == 0)
        {
            return new(CaptureStatus.NoPerformance, 0);
        }

        List<NoteEdit> changes = workspace.Changes;
        HashSet<CaptureKey> matched = workspace.Matched;
        int[] counts = workspace.Counts;

        for (int track = 0; track < TrackCount; track++)
        {
            counts[track] = _tracks[track].Notes.Count;

            for (int index = _tracks[track].Notes.Count - 1; index >= 0; index--)
            {
                StoredNote stored = _tracks[track].Notes[index];
                CaptureKey key = new(track, stored.Note.Note, stored.Note.Beat);

                if (!candidates.TryGetValue(key, out CaptureCandidate candidate))
                {
                    continue;
                }

                if (matched.Add(key))
                {
                    if (!candidate.Matches(stored.Note))
                    {
                        changes.Add(
                            new(
                                track,
                                stored.Id,
                                index,
                                stored.Note,
                                candidate.ToNote(),
                                _editingSceneId
                            )
                        );
                    }
                }

                else
                {
                    changes.Add(new(track, stored.Id, index, stored.Note, null, _editingSceneId));
                    counts[track]--;
                }
            }
        }

        List<CaptureCandidate> additions = workspace.Additions;

        foreach ((CaptureKey key, CaptureCandidate candidate) in candidates)
        {
            if (!matched.Contains(key))
            {
                counts[key.Track]++;
                additions.Add(candidate);
            }
        }

        if (
            counts.Any(count => count > MaximumNotesPerTrack)
            || TotalNoteCount() - _tracks.Sum(track => track.Notes.Count) + counts.Sum()
                > MaximumProjectNotes
        )
        {
            return new(CaptureStatus.CapacityExceeded, 0);
        }

        if (changes.Count == 0 && additions.Count == 0)
        {
            return new(CaptureStatus.NoChanges, candidates.Count);
        }

        additions.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        _history.Commit();

        foreach (NoteEdit edit in changes)
        {
            _ = workspace.ChangedIds.Add(edit.Id);
        }

        for (int index = _voices.Count - 1; index >= 0; index--)
        {
            if (_voices[index].IsLoop && workspace.ChangedIds.Contains(_voices[index].Id))
            {
                _voices.RemoveAt(index);
            }
        }

        foreach (NoteEdit edit in changes)
        {
            CommitCapturedNote(edit);
        }

        foreach (CaptureCandidate candidate in additions)
        {
            int track = candidate.Track;
            int id = NextVoiceId();
            LoopNote note = candidate.ToNote();
            _history.RecordNote(
                new(track, id, _tracks[track].Notes.Count, null, note, _editingSceneId)
            );
            _tracks[track].Notes.Add(new StoredNote(id, note, _editingSceneId));
            DeferCapturedNote(track, id);
        }

        RebuildSchedule();
        AlignHistorySchedule();
        _noteRevision++;
        _history.Commit();
        return new(CaptureStatus.Captured, candidates.Count);
    }

    private void CommitCapturedNote(NoteEdit edit)
    {
        _history.RecordNote(edit);
        ApplyHistoryNote(edit, false, false);

        if (edit.After is not null)
        {
            DeferCapturedNote(edit.Track, edit.Id);
        }
    }

    private void DeferCapturedNote(int track, int id)
    {
        if (_isPlaying && _tracks[track].ActiveSceneId == _editingSceneId)
        {
            _pendingLoopStarts[id] = new(
                (long)Math.Floor(
                    (_absoluteBeat - _tracks[track].Origin + BeatBoundaryTolerance) / LoopBeats
                )
                    + 1,
                _tracks[track].Generation
            );
        }
    }
}
