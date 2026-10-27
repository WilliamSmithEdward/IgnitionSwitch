using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;

internal static class PressureRoutingTests
{
    public static void Routing()
    {
        var router = new LaunchkeyPerformanceRouter();
        Reject(router, [0xA9, 37, 80], 1);
        Reject(router, [0xD9, 80], 1);
        Route(router, [0x99, 37, 100], 1, MidiInputKind.NoteOn, 37, 100);
        Route(router, [0xA9, 37, 80], 1, MidiInputKind.PolyphonicAftertouch, 37, 80);
        Route(router, [0xD9, 80], 1, MidiInputKind.ChannelAftertouch, 0, 80);

        foreach (int mode in new[] { 2, 9, 4 })
        {
            Route(router, [0xA9, 37, 64], mode, MidiInputKind.PolyphonicAftertouch, 37, 64);
            Route(router, [0xA9, 37, 0], mode, MidiInputKind.PolyphonicAftertouch, 37, 0);
            Route(router, [0xD9, 64], mode, MidiInputKind.ChannelAftertouch, 0, 64);
            Route(router, [0xD9, 0], mode, MidiInputKind.ChannelAftertouch, 0, 0);
            Reject(router, [0x99, 38, 100], mode);
            Reject(router, [0xA9, 38, 100], mode);
        }

        Route(router, [0x99, 37, 0], 2, MidiInputKind.NoteOff, 37, 0);
        Reject(router, [0xA9, 37, 127], 2);
        Reject(router, [0xA9, 37, 0], 1);
        Reject(router, [0xD9, 127], 2);
        Reject(router, [0xD9, 0], 1);
        VerifyIndependentOwnership();
        VerifyBoundsAndPorts();
    }

    private static void VerifyIndependentOwnership()
    {
        var router = new LaunchkeyPerformanceRouter();

        for (int note = 36; note <= 51; note++)
        {
            Route(
                router,
                [
                    0x99,
                    (byte)note,
                    100
                ],
                1,
                MidiInputKind.NoteOn,
                note,
                100
            );
            Route(
                router,
                [
                    0xA9,
                    (byte)note,
                    127
                ],
                1,
                MidiInputKind.PolyphonicAftertouch,
                note,
                127
            );
        }

        for (int note = 36; note < 51; note++)
        {
            Route(
                router,
                [
                    0x99,
                    (byte)note,
                    0
                ],
                2,
                MidiInputKind.NoteOff,
                note,
                0
            );
            Reject(
                router,
                [
                    0xA9,
                    (byte)note,
                    127
                ],
                2
            );
            Route(router, [0xD9, 100], 2, MidiInputKind.ChannelAftertouch, 0, 100);
        }

        Route(router, [0x99, 51, 0], 2, MidiInputKind.NoteOff, 51, 0);
        Reject(router, [0xD9, 100], 2);
        Route(router, [0x99, 37, 100], 1, MidiInputKind.NoteOn, 37, 100);
        Route(router, [0x99, 37, 110], 1, MidiInputKind.NoteOn, 37, 110);
        Route(router, [0x99, 37, 120], 1, MidiInputKind.NoteOn, 37, 120);
        Route(router, [0x99, 37, 0], 2, MidiInputKind.NoteOff, 37, 0);
        Reject(router, [0x99, 37, 0], 2);
        Reject(router, [0xD9, 100], 1);
        Reject(router, [0xA9, 37, 100], 1);
    }

    private static void VerifyBoundsAndPorts()
    {
        var router = new LaunchkeyPerformanceRouter();
        Route(router, [0x99, 37, 100], 1, MidiInputKind.NoteOn, 37, 100);

        foreach (byte[] raw in new byte[][]
        {
            [0xA9, 35, 100],
            [0xA9, 52, 100],
            [0xAA, 37, 100],
            [0xDA, 100],
            [0xA0, 96, 100],
            [0xA0, 64, 100],
            [0xD0, 100],
            [0xB9, 1, 100]
        })
        {
            Reject(router, raw, 1);
        }

        Check.That(
            !MidiMessageParser.TryParse([0xA9, 37, 128], false, out _)
                && !MidiMessageParser.TryParse([0xD9, 128], false, out _),
            "Malformed pressure data must not pass the parser."
        );

        foreach (MidiInputEvent invalid in new[]
        {
            new MidiInputEvent(MidiInputKind.PolyphonicAftertouch, 9, 37, -1, false),
            new MidiInputEvent(MidiInputKind.PolyphonicAftertouch, 9, 37, 128, false),
            new MidiInputEvent(MidiInputKind.ChannelAftertouch, 9, 0, -1, false),
            new MidiInputEvent(MidiInputKind.ChannelAftertouch, 9, 0, 128, false),
            new MidiInputEvent(MidiInputKind.ChannelAftertouch, 9, 37, 64, false)
        })
        {
            Check.That(
                !router.TryRoute(invalid, 1, out MidiInputEvent? rejected) && rejected is null,
                "Unvalidated values and noncanonical channel-pressure payloads must be rejected."
            );
        }

        foreach (byte[] raw in new byte[][]
        {
            [0xA0, 60, 100],
            [0xD0, 100],
            [0xB0, 1, 100]
        })
        {
            MidiInputEvent input = Parse(raw, true);
            Check.That(
                router.TryRoute(input, 2, out MidiInputEvent? routed)
                    && ReferenceEquals(input, routed),
                "Performance-port expression must pass unchanged without new ownership or a duplicate event."
            );
        }
    }

    private static void Route(
        LaunchkeyPerformanceRouter router,
        byte[] raw,
        int mode,
        MidiInputKind kind,
        int number,
        int value
    )
    {
        MidiInputEvent input = Parse(raw, false);
        Check.That(
            router.TryRoute(input, mode, out MidiInputEvent? routed)
                && routed is not null
                    && routed.SourcePerformance
                    && routed.Kind == kind
                    && routed.Channel == 9
                    && routed.Number == number
                    && routed.Value == value,
            "Owned Drum input must preserve pressure/note semantics, including zero pressure and mode transitions."
        );
        Check.That(
            !input.SourcePerformance,
            "Normalization must not mutate the original endpoint classification."
        );
    }

    private static void Reject(LaunchkeyPerformanceRouter router, byte[] raw, int mode)
    {
        Check.That(
            !router.TryRoute(Parse(raw, false), mode, out MidiInputEvent? routed) && routed is null,
            "Unowned or unsupported control-port expression must not become musical input."
        );
    }

    private static MidiInputEvent Parse(byte[] raw, bool performance)
    {
        Check.That(
            MidiMessageParser.TryParse(raw, performance, out MidiInputEvent? input),
            "A valid MIDI fixture was rejected."
        );
        return input ?? throw new InvalidOperationException("Parser returned no input.");
    }
}
