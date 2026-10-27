using System.Runtime.CompilerServices;
using System.Text.Json;
using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal static class ExpressionCommandTests
{
    private const int ControlBatchIterations = 10000;

    public static void OwnershipAndAudio()
    {
        VerifyOriginalChannel();
        VerifyReplacedOwnerAndPreview();
        VerifyOlderPreviewRelease();
        VerifyFinishedPressureOwner();
    }

    public static void LifecycleAndGuards()
    {
        VerifySustainedGate();
        VerifyWheelRetention(ResetKind.Stop);
        VerifyWheelRetention(ResetKind.Panic);
        VerifyWheelRetention(ResetKind.Load);
        VerifyWheelRetention(ResetKind.Modal);
        VerifyDisconnectAndDisposal();
        VerifyInvalidAndOfflineInput();
    }

    public static void HotPath()
    {
        using var pair = new Pair();

        for (int index = 0; index < 64; index++)
        {
            pair.Send(MidiInputKind.NoteOn, index % 16, 48 + index, 100);
        }

        MidiInputEvent[] controls =
        [
            new(MidiInputKind.ControlChange, 0, 1, 64, true),
            new(MidiInputKind.ChannelAftertouch, 0, 0, 40, true),
            new(MidiInputKind.PolyphonicAftertouch, 0, 48, 20, true),
            new(MidiInputKind.ControlChange, 0, 1, 96, true),
            new(MidiInputKind.ChannelAftertouch, 0, 0, 120, true),
            new(MidiInputKind.PolyphonicAftertouch, 0, 48, 100, true)
        ];
        int activityCount = 0;
        pair.Session.PerformanceChanged += (_, _) => activityCount++;
        int feedbackCount = pair.Controller.Feedback.Count;
        int padCount = pair.Controller.PadFeedbackEvents.Count;
        long revision = pair.Session.Revision;
        PerformanceInputDiagnostics.Snapshot diagnostics = pair.Session.ReadInputDiagnostics();
        string project = JsonSerializer.Serialize(pair.Engine.CaptureSession());

        // Warm this exact reused-message batch, including tiered-JIT/OSR transitions.
        // Parsing, message construction, rendering and assertions are outside the measurement.
        for (
            int batch = 0;
            batch < 3;
            batch++
        )
        {
            ControlBatch(pair.Controller, controls);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        ControlBatch(pair.Controller, controls);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.That(
            allocated == 0,
            $"Reused expression controls must allocate zero coordinator bytes after warmup; got {allocated}."
        );
        Check.That(
            pair.Controller.Feedback.Count == feedbackCount
                && pair.Controller.PadFeedbackEvents.Count == padCount
                && activityCount == 0
                && pair.Session.ReadInputDiagnostics() == diagnostics,
            "Wheel and pressure updates must not emit performance activity, hardware writes, or drum hits."
        );
        Check.That(
            pair.Session.Revision == revision
                && !pair.Session.HasChanges
                && !pair.Engine.CanUndo
                && !pair.Engine.CanRedo
                && project == JsonSerializer.Serialize(pair.Engine.CaptureSession()),
            "Live expression must leave project schema, content, revision and history unchanged."
        );
    }

    private static void VerifyOriginalChannel()
    {
        using var pair = new Pair();
        using WorkstationEngine neutral = CreateEngine();
        pair.Send(MidiInputKind.ControlChange, 2, 1, 127);
        pair.Send(MidiInputKind.ChannelAftertouch, 2, 0, 127);
        LiveNoteHandle keys = pair.Begin(2, 0, 60, modulation: 1);
        neutral.NoteOn(0, 60, 100);
        float[] actual = AssertAudio(
            pair,
            "An idle wheel must initialize the next channel-owned strike.",
            0.06
        );
        Check.That(
            !actual.SequenceEqual(Check.Render(neutral, 0.06)),
            "The routed modulation command must audibly change the dry reference waveform."
        );

        pair.Session.SelectTrack(1);
        pair.Send(MidiInputKind.ControlChange, 2, 1, 64);
        pair.Send(MidiInputKind.ChannelAftertouch, 2, 0, 90);
        _ = pair.Reference.SetLiveExpression(keys, 64 / 127.0, 90 / 127.0);
        _ = AssertAudio(
            pair,
            "Channel expression must follow the original gate after track selection changes."
        );
        pair.Send(MidiInputKind.PolyphonicAftertouch, 2, 60, 120);
        _ = pair.Reference.SetLiveExpression(keys, 64 / 127.0, 120 / 127.0);
        _ = AssertAudio(pair, "Poly pressure must address the original channel and pitch.");

        pair.Send(MidiInputKind.PolyphonicAftertouch, 3, 60, 127);
        pair.Send(MidiInputKind.ChannelAftertouch, 3, 0, 127);
        pair.Send(MidiInputKind.ControlChange, 3, 1, 96);
        _ = AssertAudio(
            pair,
            "Another channel's pressure and wheel must not color an existing gate."
        );
        LiveNoteHandle bass = pair.Begin(3, 1, 60, 90, 96 / 127.0);
        pair.Send(MidiInputKind.ControlChange, 2, 1, 0);
        pair.Send(MidiInputKind.ChannelAftertouch, 2, 0, 0);
        _ = pair.Reference.SetLiveExpression(keys, 0, 120 / 127.0);
        _ = AssertAudio(
            pair,
            "Simultaneous same-pitch gates on different tracks must retain channel ownership."
        );
        pair.Send(MidiInputKind.ChannelAftertouch, 3, 0, 70);
        _ = pair.Reference.SetLiveExpression(bass, 96 / 127.0, 70 / 127.0);
        _ = AssertAudio(pair, "The second channel must control only its own current gate.");
    }

    private static void VerifyReplacedOwnerAndPreview()
    {
        using var pair = new Pair();
        LiveNoteHandle first = pair.Begin(1, 0, 60);
        pair.Send(MidiInputKind.ControlChange, 1, 1, 127);
        pair.Send(MidiInputKind.ChannelAftertouch, 1, 0, 127);
        _ = pair.Reference.SetLiveExpression(first, 1, 1);
        _ = AssertAudio(pair, "The first channel must own its expressed strike.");
        _ = pair.Reference.SetLiveExpression(first, 0, 0);
        LiveNoteHandle latest = pair.Begin(2, 0, 60);
        pair.Send(MidiInputKind.ControlChange, 1, 1, 90);
        pair.Send(MidiInputKind.ChannelAftertouch, 1, 0, 127);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 1, 60, 127);
        pair.Send(MidiInputKind.NoteOff, 1, 60, 0);
        _ = AssertAudio(
            pair,
            "An older same-pitch owner must neither express nor release the newer gate."
        );
        pair.Send(MidiInputKind.ControlChange, 2, 1, 64);
        pair.Send(MidiInputKind.ChannelAftertouch, 2, 0, 64);
        _ = pair.Reference.SetLiveExpression(latest, 64 / 127.0, 64 / 127.0);
        _ = AssertAudio(pair, "The latest same-track/pitch owner must remain expressive.");

        _ = pair.Reference.SetLiveExpression(latest, 0, 0);
        pair.Session.NoteOn(0, 60, 100);
        pair.Reference.NoteOn(0, 60, 100);
        pair.Send(MidiInputKind.ControlChange, 2, 1, 127);
        pair.Send(MidiInputKind.ChannelAftertouch, 2, 0, 127);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 2, 60, 127);
        pair.Send(MidiInputKind.NoteOff, 2, 60, 0);
        _ = AssertAudio(
            pair,
            "A preview replacement must reject the prior physical owner's expression and release."
        );
        pair.Session.NoteOff(0, 60);
        pair.Reference.NoteOff(0, 60);
        _ = AssertAudio(
            pair,
            "The preview's final release must preserve the established shared-owner policy."
        );
    }

    private static void VerifyOlderPreviewRelease()
    {
        using var pair = new Pair();
        pair.Session.NoteOn(0, 60, 100);
        pair.Reference.NoteOn(0, 60, 100);
        LiveNoteHandle latest = pair.Begin(4, 0, 60);
        pair.Session.NoteOff(0, 60);
        pair.Send(MidiInputKind.ControlChange, 4, 1, 100);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 4, 60, 100);
        _ = pair.Reference.SetLiveExpression(latest, 100 / 127.0, 100 / 127.0);
        _ = AssertAudio(
            pair,
            "An older preview release must not disown or release a newer physical gate."
        );
    }

    private static void VerifyFinishedPressureOwner()
    {
        using var pair = new Pair();
        LiveNoteHandle old = pair.Begin(9, 3, 37);
        pair.Send(MidiInputKind.ChannelAftertouch, 9, 0, 127);
        _ = pair.Reference.SetLiveExpression(old, 0, 1);
        _ = AssertAudio(pair, "Pressure must reach the current drum gate.", 2);
        _ = pair.Begin(9, 3, 38);
        _ = AssertAudio(
            pair,
            "A finished gate must not leak cached channel pressure into a fresh drum hit."
        );
    }

    private static void VerifySustainedGate()
    {
        using var pair = new Pair();
        pair.Send(MidiInputKind.ControlChange, 4, 64, 127);
        pair.Reference.SetSustain(0, true);
        LiveNoteHandle gate = pair.Begin(4, 0, 60);
        pair.Send(MidiInputKind.ControlChange, 4, 1, 64);
        pair.Send(MidiInputKind.ChannelAftertouch, 4, 0, 80);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 4, 60, 127);
        _ = pair.Reference.SetLiveExpression(gate, 64 / 127.0, 1);
        _ = AssertAudio(
            pair,
            "Held pressure must combine channel and note pressure without replacing ownership."
        );
        pair.Send(MidiInputKind.NoteOff, 4, 60, 0);
        _ = pair.Reference.SetLiveExpression(gate, 64 / 127.0, 80 / 127.0);
        pair.Reference.NoteOff(0, 60);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 4, 60, 127);
        pair.Send(MidiInputKind.ControlChange, 4, 1, 100);
        pair.Send(MidiInputKind.ChannelAftertouch, 4, 0, 40);
        _ = pair.Reference.SetLiveExpression(gate, 100 / 127.0, 40 / 127.0);
        _ = AssertAudio(
            pair,
            "A sustained current gate must accept wheel/channel pressure but ignore post-keyup poly pressure."
        );

        pair.Send(MidiInputKind.ControlChange, 4, 64, 0);
        pair.Reference.SetSustain(0, false);
        pair.Send(MidiInputKind.ChannelAftertouch, 4, 0, 127);
        pair.Send(MidiInputKind.PolyphonicAftertouch, 4, 60, 127);
        _ = AssertAudio(
            pair,
            "Pedal-up must end pressure ownership rather than modify a release tail.",
            0.2
        );
        _ = pair.Begin(4, 0, 64, modulation: 100 / 127.0);
        _ = AssertAudio(
            pair,
            "Pedal-up clears channel pressure while retaining the wheel's physical position."
        );
    }

    private static void VerifyWheelRetention(ResetKind kind)
    {
        using var pair = new Pair();
        pair.Send(MidiInputKind.ControlChange, 0, 1, 100);
        LiveNoteHandle gate = pair.Begin(0, 0, 60, modulation: 100 / 127.0);
        pair.Send(MidiInputKind.ChannelAftertouch, 0, 0, 127);
        _ = pair.Reference.SetLiveExpression(gate, 100 / 127.0, 1);
        _ = AssertAudio(pair, "The reset scenario must begin with an expressed live gate.");
        SessionSnapshot snapshot = pair.Engine.CaptureSession();

        switch (kind)
        {
            case ResetKind.Stop:
                pair.Session.Stop();
                pair.Reference.Stop();
                pair.Reference.AllNotesOff();
                break;

            case ResetKind.Panic:
                pair.Send(MidiInputKind.ControlChange, 0, 123, 0);
                pair.Reference.AllNotesOff();
                break;

            case ResetKind.Load:
                pair.Session.ApplySession(snapshot);
                pair.Reference.ApplySession(snapshot);
                break;

            case ResetKind.Modal:
                pair.Session.IsInteractionBlocked = true;
                pair.Reference.Stop();
                pair.Reference.AllNotesOff();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _ = pair.Begin(0, 0, 64, modulation: 100 / 127.0);
        _ = AssertAudio(
            pair,
            $"{kind} must retain CC1 but clear prior pressure and gate ownership."
        );
        pair.Session.IsInteractionBlocked = false;
    }

    private static void VerifyDisconnectAndDisposal()
    {
        using var pair = new Pair();
        pair.Send(MidiInputKind.ControlChange, 0, 1, 127);
        _ = pair.Begin(0, 0, 60, modulation: 1);
        _ = AssertAudio(pair, "Disconnect must begin with an active modulated gate.");
        Action<MidiInputEvent> lateInput = pair.Controller.CaptureInputCallback();
        Action<MusicControlEvent> lateControl = pair.Controller.CaptureControlCallback();
        pair.Controller.SetConnection(false);
        pair.Reference.Stop();
        pair.Reference.AllNotesOff();
        long revision = pair.Session.Revision;
        int feedback = pair.Controller.Feedback.Count;
        lateInput(new(MidiInputKind.ControlChange, 0, 1, 127, true));
        lateInput(new(MidiInputKind.ChannelAftertouch, 0, 0, 127, true));
        lateInput(new(MidiInputKind.NoteOn, 0, 72, 100, true));
        lateControl(new(MusicControlKind.ToggleRecording));
        Check.That(
            pair.Session.Revision == revision
                && pair.Controller.Feedback.Count == feedback
                && !pair.Engine.IsPlaying,
            "Queued disconnected input and controls must not revive expression or mutate the project."
        );
        _ = AssertAudio(pair, "Disconnect must leave silence despite queued input.");
        pair.Controller.SetConnection(true);
        _ = pair.Begin(0, 0, 64);
        _ = AssertAudio(
            pair,
            "Reconnect must start with neutral wheel and pressure rather than stale queued values."
        );
        pair.Session.Dispose();
        lateInput(new(MidiInputKind.ControlChange, 0, 1, 127, true));
        lateInput(new(MidiInputKind.PolyphonicAftertouch, 0, 64, 127, true));
        lateControl(new(MusicControlKind.ToggleRecording));
        Check.That(
            pair.Controller.IsDisposed,
            "Disposal must complete before late callbacks are ignored."
        );
    }

    private static void VerifyInvalidAndOfflineInput()
    {
        using var pair = new Pair();
        LiveNoteHandle gate = pair.Begin(0, 0, 60);
        MidiInputEvent[] invalid =
        [
            new(MidiInputKind.ControlChange, 0, 1, 127, false),
            new(MidiInputKind.ControlChange, -1, 1, 127, true),
            new(MidiInputKind.ControlChange, 16, 1, 127, true),
            new(MidiInputKind.ControlChange, 0, 1, -1, true),
            new(MidiInputKind.ControlChange, 0, 1, 128, true),
            new(MidiInputKind.ChannelAftertouch, 0, 0, 128, true),
            new(MidiInputKind.ChannelAftertouch, 0, 7, 127, true),
            new(MidiInputKind.PolyphonicAftertouch, 0, -1, 127, true),
            new(MidiInputKind.PolyphonicAftertouch, 0, 128, 127, true),
            new(MidiInputKind.PitchBend, 0, 0, 16384, true)
        ];
        long revision = pair.Session.Revision;
        int feedback = pair.Controller.Feedback.Count;

        foreach (MidiInputEvent input in invalid)
        {
            pair.Controller.EmitInput(input);
        }

        _ = AssertAudio(pair, "Invalid or wrong-route expression must not touch a live gate.");
        Check.That(
            pair.Session.Revision == revision && pair.Controller.Feedback.Count == feedback,
            "Invalid expression must not write feedback or dirty the project."
        );
        pair.Session.Stop();
        pair.Reference.Stop();
        pair.Reference.AllNotesOff();
        SessionSnapshot snapshot = pair.Engine.CaptureSession();
        pair.Session.ApplySession(snapshot);
        pair.Reference.ApplySession(snapshot);
        pair.Audio.Dispose();
        pair.Send(MidiInputKind.ControlChange, 0, 1, 80);
        gate = pair.Begin(0, 0, 64, modulation: 80 / 127.0, retain: false);
        pair.Send(MidiInputKind.ChannelAftertouch, 0, 0, 80);
        _ = pair.Reference.SetLiveExpression(gate, 80 / 127.0, 80 / 127.0);
        _ = AssertAudio(
            pair,
            "Connected offline input must retain live expression semantics without Capture history."
        );
        Check.That(
            !pair.Engine.HasRecentPerformance,
            "Unavailable-audio notes must remain excluded from retrospective capture."
        );
    }

    private static float[] AssertAudio(Pair pair, string message, double seconds = 0.04)
    {
        float[] actual = Check.Render(pair.Engine, seconds);
        float[] expected = Check.Render(pair.Reference, seconds);
        Check.That(actual.SequenceEqual(expected), message);
        Check.That(actual.All(float.IsFinite), "Expression rendering must remain finite.");
        return actual;
    }

    private static WorkstationEngine CreateEngine()
    {
        var engine = new WorkstationEngine { Tempo = 120 };
        engine.ApplySession(engine.CaptureSession());
        return engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ControlBatch(FakeMusicController controller, MidiInputEvent[] controls)
    {
        for (int iteration = 0; iteration < ControlBatchIterations; iteration++)
        {
            foreach (MidiInputEvent input in controls)
            {
                controller.EmitInput(input);
            }
        }
    }

    private enum ResetKind
    {
        Stop,
        Panic,
        Load,
        Modal
    }

    private sealed class Pair : IDisposable
    {
        public Pair()
        {
            Engine = CreateEngine();
            Reference = CreateEngine();
            Session = new StudioSession(Controller, Engine, Audio);
        }

        public FakeMusicController Controller { get; } = new();
        public FakeAudioPlayback Audio { get; } = new();
        public WorkstationEngine Engine { get; }
        public WorkstationEngine Reference { get; }
        public StudioSession Session { get; }

        public LiveNoteHandle Begin(
            int channel,
            int track,
            int note,
            int velocity = 100,
            double modulation = 0,
            double pressure = 0,
            bool retain = true
        )
        {
            Session.SelectTrack(track);
            Send(MidiInputKind.NoteOn, channel, note, velocity);
            return Reference.BeginLiveNote(track, note, velocity, retain, modulation, pressure);
        }

        public void Send(MidiInputKind kind, int channel, int number, int value)
        {
            Controller.EmitInput(new(kind, channel, number, value, true));
        }

        public void Dispose()
        {
            Session.Dispose();
            Reference.Dispose();
        }
    }
}
