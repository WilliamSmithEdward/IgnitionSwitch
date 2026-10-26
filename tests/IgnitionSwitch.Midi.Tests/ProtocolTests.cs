using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Protocol;

internal static class ProtocolTests
{
    public static void ChannelMessages()
    {
        AssertMessage([0x92, 60, 100], true, MidiInputKind.NoteOn, 2, 60, 100);
        AssertMessage([0x9F, 60, 0], false, MidiInputKind.NoteOff, 15, 60, 0);
        AssertMessage([0x80, 0, 127], true, MidiInputKind.NoteOff, 0, 0, 127);
        AssertMessage([0xBF, 115, 127], false, MidiInputKind.ControlChange, 15, 115, 127);
        AssertMessage([0xEF, 0, 64], true, MidiInputKind.PitchBend, 15, 0, 8192);
        AssertMessage([0xE0, 127, 127], true, MidiInputKind.PitchBend, 0, 0, 16383);
        AssertMessage([0xE0, 0, 0], true, MidiInputKind.PitchBend, 0, 0, 0);
        AssertMessage([0xA9, 36, 88], true, MidiInputKind.PolyphonicAftertouch, 9, 36, 88);
        AssertMessage([0xD1, 88], true, MidiInputKind.ChannelAftertouch, 1, 0, 88);
        AssertMessage([0xC1, 42], true, MidiInputKind.ProgramChange, 1, 42, 42);
        (byte Status, MidiInputKind Kind)[] transport =
        [
            (0xF8, MidiInputKind.Clock),
            (0xFA, MidiInputKind.Start),
            (0xFB, MidiInputKind.Continue),
            (0xFC, MidiInputKind.Stop)
        ];

        foreach ((byte status, MidiInputKind kind) in transport)
        {
            AssertMessage([status], false, kind, 0, 0, 0);
        }
    }

    private static void AssertMessage(
        byte[] bytes,
        bool performance,
        MidiInputKind kind,
        int channel,
        int number,
        int value
    )
    {
        Check.That(
            MidiMessageParser.TryParse(bytes, performance, out MidiInputEvent? note),
            $"Valid message {Convert.ToHexString(bytes)} was rejected."
        );
        MidiInputEvent parsed = note
            ?? throw new InvalidOperationException("Parser returned no message.");
        Check.That(parsed.Kind == kind, $"Expected message kind {kind}, got {parsed.Kind}.");
        Check.That(parsed.Channel == channel, $"Expected channel {channel}, got {parsed.Channel}.");
        Check.That(parsed.Number == number, $"Expected number {number}, got {parsed.Number}.");
        Check.That(parsed.Value == value, $"Expected value {value}, got {parsed.Value}.");
        Check.That(parsed.SourcePerformance == performance, "Source port was not preserved.");
    }

    public static void MalformedMessages()
    {
        byte[][] malformed =
        [
            [],
            [0x90],
            [0x90, 60],
            [0x90, 60, 1, 2],
            [0x90, 128, 1],
            [0x90, 60, 128],
            [0x70, 60, 1],
            [0xC0, 1, 2],
            [0xF0, 1, 0xF7],
            [0xF8, 0],
            [0xFF]
        ];

        foreach (byte[] bytes in malformed)
        {
            Check.That(
                !MidiMessageParser.TryParse(bytes, true, out MidiInputEvent? input)
                    && input is null,
                "Malformed input was accepted."
            );
        }

        var random = new Random(65037);

        for (int i = 0; i < 20000; i++)
        {
            byte[] bytes = new byte[random.Next(0, 9)];
            random.NextBytes(bytes);

            if (MidiMessageParser.TryParse(bytes, true, out MidiInputEvent? input))
            {
                MidiInputEvent parsed = input
                    ?? throw new InvalidOperationException("Parser returned no message.");
                Check.That(parsed.Channel is >= 0 and <= 15, "Parser produced an invalid channel.");
                Check.That(parsed.Number is >= 0 and <= 127, "Parser produced an invalid number.");
                Check.That(parsed.Value is >= 0 and <= 16383, "Parser produced an invalid value.");
                Check.That(
                    parsed.SourcePerformance,
                    "Parser did not preserve the performance port."
                );
                Check.That(bytes.Length is >= 1 and <= 3, "Parser accepted an unbounded message.");
            }
        }
    }

    public static void Identity()
    {
        byte[] valid = [0xF0, 0x7E, 0, 0x06, 0x02, 0, 0x20, 0x29, 0x35, 1, 0, 0, 0, 1, 2, 3, 0xF7];
        Check.That(
            LaunchkeyMk3Protocol.TryReadIdentity(valid, out ControllerIdentity? identity),
            "Valid hardware identity was rejected."
        );
        Check.That(identity?.Manufacturer == "Novation", "Manufacturer decoded incorrectly.");
        Check.That(identity?.Model == "Launchkey 37 MK3", "Model decoded incorrectly.");
        Check.That(identity?.Firmware == "0.1.2.3", "Firmware decoded incorrectly.");

        foreach (int index in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 16 })
        {
            byte[] changed = [.. valid];
            changed[index] ^= 0x01;
            Check.That(
                !LaunchkeyMk3Protocol.TryReadIdentity(changed, out _),
                "Mismatched identity was accepted."
            );
        }

        byte[] bootloader = [.. valid];
        bootloader[9] = 0x11;
        Check.That(
            !LaunchkeyMk3Protocol.TryReadIdentity(bootloader, out _),
            "Bootloader must not receive application control messages."
        );
        byte[] invalidVersion = [.. valid];
        invalidVersion[12] = 127;
        Check.That(
            !LaunchkeyMk3Protocol.TryReadIdentity(invalidVersion, out _),
            "Invalid firmware digits were accepted."
        );
        Check.That(
            !LaunchkeyMk3Protocol.TryReadIdentity(valid.AsSpan(0, 16), out _),
            "Truncated identity was accepted."
        );
        Check.That(
            !LaunchkeyMk3Protocol.TryReadIdentity([.. valid, 0], out _),
            "Oversized identity was accepted."
        );
        Check.Bytes([0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7], LaunchkeyMk3Protocol.IdentityInquiry());
    }

    public static void Feedback()
    {
        Check.Bytes([0x9F, 0x0C, 0x7F], LaunchkeyMk3Protocol.DawMode(true));
        Check.Bytes([0x9F, 0x0C, 0], LaunchkeyMk3Protocol.DawMode(false));
        Check.Bytes([0xBF, 3, 2], LaunchkeyMk3Protocol.SessionPadMode());
        Check.Bytes([0xBF, 9, 2], LaunchkeyMk3Protocol.DevicePotMode());
        Check.Bytes(
            [0xF0, 0, 0x20, 0x29, 2, 0x0F, 4, 0, 0x4B, 0x65, 0x79, 0x73, 0xF7],
            LaunchkeyMk3Protocol.SetDisplayRow(0, "Keys")
        );
        Check.Bytes([0x90, 96, 5], LaunchkeyMk3Protocol.SetSessionPad(0, 5));
        Check.Bytes([0x90, 112, 5], LaunchkeyMk3Protocol.SetSessionPad(8, 5));
        Check.Bytes([0x92, 119, 45], LaunchkeyMk3Protocol.SetSessionPad(15, 45, true));
        Check.Bytes([0x99, 36, 45], LaunchkeyMk3Protocol.SetDrumPad(36, 45));
        Check.Bytes([0x99, 51, 0], LaunchkeyMk3Protocol.SetDrumPad(51, 0));
        Check.Bytes([0x9B, 37, 5], LaunchkeyMk3Protocol.SetDrumPad(37, 5, true));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetDrumPad(35, 45));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetDrumPad(52, 45));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetDrumPad(36, -1));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetDrumPad(36, 128));
        byte[] sanitized = LaunchkeyMk3Protocol.SetDisplayRow(
            1,
            "é\n\u00F7\u00F7" + new string('x', 50)
        );
        Check.That(
            sanitized.Length == 25 && sanitized[7] == 1 && sanitized[^1] == 0xF7,
            "LCD payload was not bounded."
        );
        Check.That(
            sanitized[8..^1].All(value => value is >= 32 and <= 126),
            "LCD text escaped the printable ASCII range."
        );
        Check.That(
            LaunchkeyMk3Protocol.SetKnobName(0, "Volume")[7] == 0x38,
            "First pot LCD index was incorrect."
        );
        Check.That(
            LaunchkeyMk3Protocol.SetKnobValue(7, "100%")[7] == 0x3F,
            "Last pot LCD index was incorrect."
        );
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetSessionPad(-1, 1));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetSessionPad(16, 1));
        Check.Throws<ArgumentOutOfRangeException>(() => LaunchkeyMk3Protocol.SetSessionPad(0, 128));
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetDisplayRow(2, "Test")
        );
        Check.Throws<ArgumentOutOfRangeException>(
            () => LaunchkeyMk3Protocol.SetKnobName(8, "Test")
        );
    }
}
