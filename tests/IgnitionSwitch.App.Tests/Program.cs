using IgnitionSwitch.App;

(string Name, Action Run)[] tests =
[
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
