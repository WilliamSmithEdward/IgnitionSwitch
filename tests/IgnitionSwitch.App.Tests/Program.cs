using IgnitionSwitch.App;

(string Name, Action Run)[] tests =
[
    (
        "Scene editing, copies, exact clearing and structural history retain all rows",
        SceneCommandTests.EditingAndHistory
    ),
    (
        "Queued scene launches and lane stops preserve live ownership and saved revisions",
        SceneCommandTests.LaunchesAndOwnership
    ),
    (
        "Session context modes and scene controls honor modal, offline and shutdown guards",
        SceneCommandTests.SurfaceAndModalGuards
    ),
    (
        "Exclusive buffer requests validate and round the driver's minimum period",
        ExclusiveAudioBufferTests.MinimumPeriod
    ),
    (
        "Received drum MIDI diagnostics retain every input and reset independently of music state",
        InputDiagnosticsTests.CountingAndReset
    ),
    (
        "Sound pages share real parameter targets, revision, undo, locks, and modal lifecycle",
        SoundControlTests.PagesAndLifecycle
    ),
    (
        "Every device knob and mixer bank controls its advertised parameter",
        SoundControlTests.HardwareBanks
    ),
    (
        "Absolute knob pickup follows actual targets, external edits, and reconnection",
        SoundControlTests.Pickup
    ),
    (
        "Capture commands preserve all-track ownership, undo, saves, modal guards, and project boundaries",
        CaptureCommandTests.Commands
    ),
    (
        "Capture retains valid history through audio outages without inventing unclocked performance",
        CaptureCommandTests.AudioAvailabilityAndLifecycle
    ),
    (
        "Loop commands preserve modal, recording, live ownership, save, and terminal behavior",
        LoopCommandTests.Commands
    ),
    (
        "Hardware and UI Quantise share undo, revision, and modal behavior",
        QuantizeCommandTests.Commands
    ),
    (
        "Pad activity retains fast taps, retriggers, and matching hardware feedback",
        PerformanceFeedbackTests.Activity
    ),
    (
        "UI and hardware Undo/Redo preserve revision and modal semantics",
        PerformanceFeedbackTests.HistoryCommands
    ),
    (
        "Shared audio buffer requests follow driver periods with a positive bounded fallback",
        AudioBufferTests.SharedPeriod
    ),
    (
        "Track arrows navigate correctly, note ownership survives selection, and Drum-mode pads play drums",
        StudioSessionTests.Ownership
    ),
    (
        "Held sustain follows newly selected melodic tracks and releases all owners",
        StudioSessionTests.Sustain
    ),
    (
        "UI and normalized hardware transport pause/resume equally without losing note or pedal ownership",
        StudioSessionTests.GenericPlaybackPauseResume
    ),
    (
        "Modal transactions preserve live performance while blocking project mutations",
        StudioSessionTests.ModalIsolation
    ),
    (
        "Rapid 37-key chords and retriggers preserve every recorded input and release",
        InputReliabilityTests.RapidChords
    ),
    (
        "Overlapping MIDI channels retain independent release ownership",
        InputReliabilityTests.ChannelOwnership
    ),
    (
        "Project replacement resets modal live note and pedal ownership atomically",
        InputReliabilityTests.ProjectReplacement
    ),
    (
        "Saved revisions retain newer edits and finalized durations advance revision",
        StudioSessionTests.Revisions
    ),
    (
        "Feedback follows mute flags, track names, and connection changes",
        StudioSessionTests.Feedback
    ),
    (
        "Disposal detaches adapters and ignores previously queued callbacks",
        StudioSessionTests.Shutdown
    )
];

int failures = 0;

foreach ((string name, Action run) in tests)
{
    try
    {
        run();
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
