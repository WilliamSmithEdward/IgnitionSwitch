(string Name, Func<Task> Run)[] tests =
[
    ("Playable instruments produce bounded finite stereo audio", AudioTests.Instruments),
    ("Sustain, note ownership, retrigger, and panic release voices", AudioTests.Release),
    ("Transport advances from audio samples and wraps four bars", SequencingTests.Transport),
    ("Recording quantizes, replaces overdubs, and replays", SequencingTests.Recording),
    ("Notes spanning the loop boundary preserve their duration", SequencingTests.WrappingNote),
    ("Track mute, clear, and gain affect playback", SequencingTests.Tracks),
    ("Invalid controls and buffers fail before rendering", ValidationTests.Controls),
    (
        "Malformed, unsupported, oversized, and nonfinite projects are rejected",
        ValidationTests.Projects
    ),
    ("Project roundtrip is independent, atomic, and cancellable", PersistenceTests.Roundtrip)
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
