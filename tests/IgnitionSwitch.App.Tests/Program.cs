using IgnitionSwitch.App;

(string Name, Action Run)[] tests =
[
    (
        "MIDI note ownership survives track selection and separates drums",
        StudioSessionTests.Ownership
    ),
    (
        "Held sustain follows newly selected melodic tracks and releases all owners",
        StudioSessionTests.Sustain
    ),
    (
        "Modal transactions finalize recording and block new hardware mutations",
        StudioSessionTests.ModalIsolation
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
