(string Name, Action Run)[] tests =
[
    ("Channel messages preserve channel, port, and value semantics", ProtocolTests.ChannelMessages),
    (
        "Malformed and random input stays bounded and cannot escape the parser",
        ProtocolTests.MalformedMessages
    ),
    (
        "Identity verification rejects other models, bootloaders, and malformed replies",
        ProtocolTests.Identity
    ),
    ("Outgoing LCD and pad messages match documented hardware bytes", ProtocolTests.Feedback),
    (
        "Discovery recognizes known Windows names and rejects other product families",
        EndpointMatchingTests.Names
    ),
    (
        "Endpoint pairing never crosses containers or guesses ambiguous ports",
        EndpointMatchingTests.Pairing
    ),
    (
        "Hardware commands normalize values and reject wrong channels and unsupported modes",
        ControlMappingTests.Commands
    ),
    (
        "Captured Drum-mode input routes as performance and retains releases across mode changes",
        PerformanceRoutingTests.Routing
    ),
    ("Pad layouts are immutable and follow the physical MK3 note order", PadFeedbackTests.Layout),
    ("Pad hits coalesce bursts and retain rapid taps and held notes", PadFeedbackTests.Hits),
    ("Pad feedback respects layout, mute, retrigger, and reset state", PadFeedbackTests.Context)
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

Console.WriteLine(
    $"{tests.Length - failures}/{tests.Length} checks passed. No hardware ports opened."
);
return failures == 0 ? 0 : 1;
