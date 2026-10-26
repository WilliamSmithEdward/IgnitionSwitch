using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;

internal static class PerformanceRoutingTests
{
    public static void Routing()
    {
        var router = new LaunchkeyPerformanceRouter();
        var keyboard = new MidiInputEvent(MidiInputKind.NoteOn, 0, 60, 100, true);
        Check.That(
            router.TryRoute(keyboard, 2, out MidiInputEvent? performance)
                && ReferenceEquals(keyboard, performance),
            "Performance-port input must pass unchanged without a duplicate event."
        );
        var pedal = new MidiInputEvent(MidiInputKind.ControlChange, 0, 64, 127, true);
        Check.That(
            router.TryRoute(pedal, 1, out performance) && ReferenceEquals(pedal, performance),
            "Performance-port controls must retain their original routing."
        );

        Check.That(
            MidiMessageParser.TryParse([0x99, 37, 126], false, out MidiInputEvent? drumOn)
                && router.TryRoute(drumOn!, 1, out performance)
                && !drumOn!.SourcePerformance,
            "The captured control-port Drum press must become musical input without changing the raw event."
        );
        VerifyDrumNote(performance, MidiInputKind.NoteOn, 37, 126);
        Check.That(
            MidiMessageParser.TryParse([0x99, 37, 0], false, out MidiInputEvent? drumOff)
                && drumOff!.Kind == MidiInputKind.NoteOff
                && router.TryRoute(drumOff, 2, out performance),
            "A velocity-zero release must survive switching from Drum to Session."
        );
        VerifyDrumNote(performance, MidiInputKind.NoteOff, 37, 0);
        Check.That(
            !router.TryRoute(new(MidiInputKind.NoteOff, 9, 37, 0, false), 2, out performance)
                && performance is null,
            "An already released pad must not retain ownership outside Drum mode."
        );

        foreach (int note in new[] { 36, 51 })
        {
            Check.That(
                router.TryRoute(new(MidiInputKind.NoteOn, 9, note, 100, false), 1, out _),
                "Both boundaries of the documented Drum matrix must play."
            );
        }

        Check.That(
            router.TryRoute(new(MidiInputKind.NoteOff, 9, 36, 64, false), 9, out performance)
                && performance is { Kind: MidiInputKind.NoteOff, Value: 64 },
            "The first pad's release must preserve its velocity after a layout change."
        );
        Check.That(
            !router.TryRoute(new(MidiInputKind.NoteOn, 9, 38, 100, false), 9, out _),
            "An unsupported layout must not create new drum ownership."
        );
        Check.That(
            router.TryRoute(new(MidiInputKind.NoteOff, 9, 51, 0, false), 2, out _),
            "Independent pad ownership must retain the remaining release."
        );

        MidiInputEvent[] rejected =
        [
            new(MidiInputKind.NoteOn, 0, 96, 127, false),
            new(MidiInputKind.NoteOn, 10, 37, 127, false),
            new(MidiInputKind.NoteOn, 9, 35, 127, false),
            new(MidiInputKind.NoteOn, 9, 52, 127, false),
            new(MidiInputKind.NoteOn, 9, 37, -1, false),
            new(MidiInputKind.NoteOn, 9, 37, 128, false),
            new(MidiInputKind.ControlChange, 9, 37, 127, false),
            new(MidiInputKind.PolyphonicAftertouch, 9, 37, 127, false)
        ];

        foreach (MidiInputEvent input in rejected)
        {
            Check.That(
                !router.TryRoute(input, 1, out performance) && performance is null,
                "Session controls, other channels, and unvalidated Drum data must not become notes."
            );
        }

        Check.That(
            !router.TryRoute(new(MidiInputKind.NoteOn, 9, 37, 127, false), 2, out _),
            "Session mode must not route a control-port note as a new drum press."
        );
        Check.Throws<ArgumentNullException>(() => router.TryRoute(null!, 1, out _));
    }

    private static void VerifyDrumNote(
        MidiInputEvent? input,
        MidiInputKind kind,
        int note,
        int value
    )
    {
        Check.That(
            input is { SourcePerformance: true, Channel: 9 }
                && input.Kind == kind
                && input.Number == note
                && input.Value == value,
            "Normalized Drum input must preserve its channel, note, velocity, and release semantics."
        );
    }
}
