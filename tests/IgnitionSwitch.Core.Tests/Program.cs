(string Name, Func<Task> Run)[] tests =
[
    (
        "Live expression preserves exact neutral audio and validates transient controls",
        ExpressionTests.NeutralAndValidation
    ),
    (
        "Live modulation and pressure affect pitch and timbre independently",
        ExpressionTests.MusicalResponse
    ),
    (
        "Opaque live handles preserve sustained ownership and reject stale voices",
        ExpressionTests.OwnershipAndLifecycle
    ),
    (
        "Expression ramps preserve callback partitions and warm zero allocation",
        ExpressionTests.PartitionsAndAllocation
    ),
    ("Scenes own clip content with bounded structural history", SceneTests.ContentAndHistory),
    ("Scene schema migration and total capacity are atomic", SceneTests.PersistenceAndCapacity),
    ("Quantized clip queues preserve independent transport state", SceneTests.QueuesAndTransport),
    (
        "Recording follows active clips and splits held gates at launches",
        SceneTests.RecordingOwnership
    ),
    (
        "Empty scene launches and recording rearm retain the first performed hit",
        SceneTests.EmptyRecordingWorkflow
    ),
    ("Capture aligns mixed clip phases and retains addressed undo", SceneTests.CaptureAlignment),
    (
        "Clip launches preserve live audio and callback partition equivalence",
        SceneTests.AudioAndPartitions
    ),
    ("Playable instruments produce bounded finite stereo audio", AudioTests.Instruments),
    (
        "Normalized mixer targets share metadata, validation, stereo routing, and history",
        SoundTests.Mixer
    ),
    (
        "Tone, attack, release, drive, and drum tail change actual instrument audio",
        SoundTests.Instruments
    ),
    (
        "Stereo sends and returns preserve independent live and sequenced effect lanes",
        SoundTests.EffectsAndLanes
    ),
    ("Strict legacy JSON migrates to native dry version-two sound defaults", SchemaTests.Migration),
    (
        "Version-two sound targets roundtrip and reject malformed settings atomically",
        SchemaTests.RoundtripAndValidation
    ),
    (
        "Render quanta preserve offset guards, clock continuity, and callback partition output",
        AudioTests.RenderPartitions
    ),
    ("Sustain, note ownership, retrigger, and panic release voices", AudioTests.Release),
    (
        "Live attacks survive dense scheduled voice pressure within the 64-voice bound",
        VoicePriorityTests.LiveAttacks
    ),
    ("Transport advances from audio samples and wraps four bars", SequencingTests.Transport),
    ("Loop-off finishes the current pass and preserves transport preferences", LoopTests.Transport),
    (
        "Natural loop completion retains live ownership, recording, restart, and callback equivalence",
        LoopTests.RecordingAndPartitions
    ),
    (
        "Pause freezes time, finalizes recording, and resumes without stranded loop voices",
        SequencingTests.Pause
    ),
    ("Recording quantizes, replaces overdubs, and replays", SequencingTests.Recording),
    (
        "Recording quantization is optional, per-note, transient, and bounded",
        QuantizationTests.RecordingPolicy
    ),
    (
        "Selected-track Quantise preserves durations, ownership, collisions, and undo",
        QuantizationTests.SelectedTrack
    ),
    (
        "Incremental recording preserves ordered playback, burst releases, capacity, and note revisions",
        InputStressTests.OrderedInputs
    ),
    ("Notes spanning the loop boundary preserve their duration", SequencingTests.WrappingNote),
    ("Track mute, clear, and gain affect playback", SequencingTests.Tracks),
    (
        "Recording, held durations, replacement, and clear undo in chronological groups",
        HistoryTests.Notes
    ),
    (
        "Mixer gestures coalesce, redo branches remain valid, and project loads reset history",
        HistoryTests.MixerAndLifecycle
    ),
    ("Undo history remains bounded by step and note-delta budgets", HistoryTests.Bounds),
    ("Invalid controls and buffers fail before rendering", ValidationTests.Controls),
    (
        "Malformed, unsupported, oversized, and nonfinite projects are rejected",
        ValidationTests.Projects
    ),
    ("Project roundtrip is independent, atomic, and cancellable", PersistenceTests.Roundtrip),
    (
        "Capture free phrases preserve bar boundaries and frozen idle windows",
        CaptureAlignmentTests.FreePhraseAndBoundary
    ),
    (
        "Capture project alignment, transport epochs, and held adoption remain consistent",
        CaptureAlignmentTests.ProjectPhaseAndEpoch
    ),
    (
        "Capture merges deterministic candidates with one reversible history batch",
        CaptureTests.MergeAndHistory
    ),
    (
        "Capture capacity, truncation, and lifecycle changes are atomic and bounded",
        CaptureTests.BoundsAndLifecycle
    ),
    (
        "Capture clocks integrate rendered tempo and preserve callback partitions",
        CaptureTests.ClocksAndPartitions
    )
];

int failures = 0;

foreach ((string name, Func<Task> run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }

    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;
