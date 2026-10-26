using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Foundation;

namespace IgnitionSwitch.Midi.Devices;

/// <summary>Owns all Windows handles and serializes output against disconnect and driver callbacks.</summary>
internal sealed class ControllerSession : IDisposable
{
    private const int PadFeedbackIntervalMilliseconds = 8;
    private static readonly int[] SessionButtons = [
        LaunchkeyMk3Protocol.SceneLaunch, LaunchkeyMk3Protocol.StopSoloMute,
        LaunchkeyMk3Protocol.SceneUp, LaunchkeyMk3Protocol.SceneDown
    ];
    private readonly Lock portLock = new();
    private readonly Lock mappingLock = new();
    private readonly Lock activityLock = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly PadHitState padHits = new(LaunchkeyMk3Profile.Model37.DrumPadLayout);
    private readonly int[] lastPadColors = new int[16];
    private readonly LaunchkeyLedFeedback[] lastSessionPads = new LaunchkeyLedFeedback[16];
    private readonly LaunchkeyLedFeedback[] lastSessionButtons = new LaunchkeyLedFeedback[4];
    private readonly string?[] lastDisplayRows = new string?[2];
    private readonly Timer activityTimer;
    private readonly Action<ControllerSession> feedbackFailed;
    private readonly List<(
        MidiInPort Port,
        TypedEventHandler<MidiInPort, MidiMessageReceivedEventArgs> Handler
    )> inputs = [];
    private readonly List<DeviceWatcher> watchers = [];
    private IMidiOutPort? performanceOutput;
    private IMidiOutPort? dawOutput;
    private ControllerFeedback? previousFeedback;
    private ControllerFeedback? pendingFeedback;
    private bool enteredDawMode;
    private bool disposed;
    private bool activityDisposed;
    private bool activityScheduled;
    private bool activityPumpRunning;
    private bool activityDirty;
    private bool modeFeedbackRequested;
    private bool sessionLightsValid;
    private int padMode = 2;
    private readonly LaunchkeyControlMapper mapper = new();
    private readonly LaunchkeyPerformanceRouter performanceRouter = new();

    public CancellationToken CancellationToken { get; }

    public ControllerSession(Action<ControllerSession> feedbackFailed)
    {
        this.feedbackFailed = feedbackFailed;
        CancellationToken = cancellation.Token;
        activityTimer = new Timer(
            static state => ((ControllerSession)state!).PumpPadFeedback(),
            this,
            Timeout.Infinite,
            Timeout.Infinite
        );
    }

    public TaskCompletionSource<ControllerIdentity> IdentityReply { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public bool HasDawOutput
    {
        get
        {
            lock (portLock)
            {
                return dawOutput is not null && !disposed;
            }
        }
    }

    public void AttachInput(
        MidiInPort port,
        TypedEventHandler<MidiInPort, MidiMessageReceivedEventArgs> handler
    )
    {
        lock (portLock)
        {
            if (disposed)
            {
                port.Dispose();
                throw new OperationCanceledException();
            }

            inputs.Add((port, handler));
            port.MessageReceived += handler;
        }
    }

    public void AttachOutput(IMidiOutPort port, bool performance)
    {
        lock (portLock)
        {
            if (disposed)
            {
                port.Dispose();
                throw new OperationCanceledException();
            }

            if (performance)
            {
                performanceOutput = port;
            }

            else
            {
                dawOutput = port;
            }
        }
    }

    public void SendPerformance(byte[] message)
    {
        lock (portLock)
        {
            CancellationToken.ThrowIfCancellationRequested();
            performanceOutput?.SendBuffer(message.AsBuffer());
        }
    }

    public void EnterDawMode()
    {
        lock (portLock)
        {
            CancellationToken.ThrowIfCancellationRequested();
            SendDaw(LaunchkeyMk3Protocol.DawMode(true));
            enteredDawMode = true;
            SendDaw(LaunchkeyMk3Protocol.SessionPadMode());
            SendDaw(LaunchkeyMk3Protocol.DevicePotMode());
            var initial = new ControllerFeedback(0, false, false, ["IgnitionSwitch"], [false]);

            for (int knob = 0; knob < 8; knob++)
            {
                ControllerKnobFeedback value = LaunchkeyFeedbackPresentation.KnobAt(initial, knob);
                SendDaw(LaunchkeyMk3Protocol.SetKnobName(knob, value.Name));
                SendDaw(LaunchkeyMk3Protocol.SetKnobValue(knob, value.Value));
            }

            SendDaw(LaunchkeyMk3Protocol.SetDeviceLock(false));
            SendDaw(LaunchkeyMk3Protocol.SetDisplayRow(0, "IgnitionSwitch"));
            SendDaw(LaunchkeyMk3Protocol.SetDisplayRow(1, "Loop ready"));
        }
    }

    public bool TryMapControl(
        MidiInputEvent input,
        out MusicControlEvent? control,
        out MidiInputEvent routed
    )
    {
        bool changedMode;
        bool mapped;

        lock (mappingLock)
        {
            int oldPadMode = mapper.PadMode;
            int oldPotMode = mapper.PotMode;
            mapped = mapper.TryMap(input, out control);
            routed = performanceRouter.TryRoute(input, mapper.PadMode, out MidiInputEvent? musical)
                ? musical!
                : input;
            changedMode = oldPadMode != mapper.PadMode || oldPotMode != mapper.PotMode;
            Volatile.Write(ref padMode, mapper.PadMode);
        }

        if (changedMode)
        {
            lock (activityLock)
            {
                if (!activityDisposed)
                {
                    modeFeedbackRequested = true;
                    RequestPadFrame();
                }
            }
        }

        return mapped;
    }

    public void UpdatePadFeedback(PadFeedback feedback)
    {
        lock (activityLock)
        {
            if (!activityDisposed && padHits.Update(feedback, Environment.TickCount64))
            {
                RequestPadFrame();
            }
        }
    }

    public void WriteFeedback(ControllerFeedback feedback)
    {
        lock (activityLock)
        {
            if (activityDisposed)
            {
                return;
            }

            pendingFeedback = feedback;
            RequestPadFrame();
        }
    }

    private void PaintFeedback(ControllerFeedback feedback)
    {
        if (disposed || !enteredDawMode || dawOutput is null)
        {
            return;
        }

        if (LaunchkeyFeedbackPresentation.HasSameState(previousFeedback, feedback))
        {
            return;
        }

        ControllerFeedback? previous = previousFeedback;
        int mode = Volatile.Read(ref padMode);
        bool sameTracks = LaunchkeyFeedbackPresentation.HasSameTrackState(previous, feedback);

        if (mode == 2)
        {
            PaintSession(feedback);
        }

        else if (mode == 1 && !sameTracks)
        {
            PaintDrumPads(feedback, true);
        }

        else if (mode == 9 && (previous is null || previous.DeviceName != feedback.DeviceName))
        {
            for (int pad = 0; pad < 16; pad++)
            {
                if (Volatile.Read(ref padMode) != mode)
                {
                    break;
                }

                // Every current sound device has one parameter page. Other raw pad indices
                // are not advertised as additional devices or nonexistent parameter pages.
                int color = pad == 0 && !string.IsNullOrEmpty(feedback.DeviceName) ? 49 : 0;
                SendDaw(LaunchkeyMk3Protocol.SetDeviceSelectPad(pad, color));
            }
        }

        if (previous is null || previous.DeviceLocked != feedback.DeviceLocked)
        {
            SendDaw(LaunchkeyMk3Protocol.SetDeviceLock(feedback.DeviceLocked));
        }

        for (int knob = 0; knob < 8; knob++)
        {
            ControllerKnobFeedback value = LaunchkeyFeedbackPresentation.KnobAt(feedback, knob);
            ControllerKnobFeedback? oldValue = previous is null
                ? null
                : LaunchkeyFeedbackPresentation.KnobAt(previous, knob);

            if (oldValue?.Name != value.Name)
            {
                SendDaw(LaunchkeyMk3Protocol.SetKnobName(knob, value.Name));
            }

            if (oldValue?.Value != value.Value)
            {
                SendDaw(LaunchkeyMk3Protocol.SetKnobValue(knob, value.Value));
            }
        }

        PaintDisplayRow(0, LaunchkeySessionPresentation.DisplayTitle(feedback, mode));
        PaintDisplayRow(1, LaunchkeySessionPresentation.DisplayDetail(feedback, mode));
        previousFeedback = feedback;
    }

    private void PaintSession(ControllerFeedback feedback)
    {
        for (int pad = 0; pad < lastSessionPads.Length; pad++)
        {
            if (Volatile.Read(ref padMode) != 2)
            {
                return;
            }

            LaunchkeyLedFeedback light = LaunchkeySessionPresentation.Pad(feedback, pad);

            if (!sessionLightsValid || light != lastSessionPads[pad])
            {
                SendDaw(LaunchkeyMk3Protocol.SetSessionPad(pad, light));
                lastSessionPads[pad] = light;
            }
        }

        for (int index = 0; index < SessionButtons.Length; index++)
        {
            if (Volatile.Read(ref padMode) != 2)
            {
                return;
            }

            int controller = SessionButtons[index];
            LaunchkeyLedFeedback light = LaunchkeySessionPresentation.Button(feedback, controller);

            if (!sessionLightsValid || light != lastSessionButtons[index])
            {
                SendDaw(LaunchkeyMk3Protocol.SetRgbControl(controller, light));
                lastSessionButtons[index] = light;
            }
        }

        sessionLightsValid = true;
    }

    private void PaintDisplayRow(int row, string text)
    {
        if (lastDisplayRows[row] != text)
        {
            SendDaw(LaunchkeyMk3Protocol.SetDisplayRow(row, text));
            lastDisplayRows[row] = text;
        }
    }

    public void StartWatchers(Action onRemoved)
    {
        lock (portLock)
        {
            if (disposed)
            {
                return;
            }

            var ids = inputs
                .Select(input => input.Port.DeviceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (performanceOutput is not null)
            {
                _ = ids.Add(performanceOutput.DeviceId);
            }

            if (dawOutput is not null)
            {
                _ = ids.Add(dawOutput.DeviceId);
            }

            foreach (string? selector in new[]
            {
                MidiInPort.GetDeviceSelector(),
                MidiOutPort.GetDeviceSelector()
            })
            {
                DeviceWatcher watcher = DeviceInformation.CreateWatcher(selector);
                watcher.Removed += (sender, args) =>
                {
                    if (ids.Contains(args.Id))
                    {
                        onRemoved();
                    }
                };
                watchers.Add(watcher);
                watcher.Start();
            }
        }
    }

    public void Dispose()
    {
        lock (activityLock)
        {
            if (activityDisposed)
            {
                return;
            }

            activityDisposed = true;
            pendingFeedback = null;
            padHits.Clear();
        }

        activityTimer.Dispose();

        lock (portLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Cancel();
            cancellation.Dispose();

            foreach (DeviceWatcher watcher in watchers)
            {
                TryClose(() =>
                {
                    if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    {
                        watcher.Stop();
                    }
                });
            }

            if (enteredDawMode)
            {
                TryClose(() =>
                {
                    SendDaw(LaunchkeyMk3Protocol.ClearDisplay());
                    SendDaw(LaunchkeyMk3Protocol.DawMode(false));
                });
            }

            foreach ((
                MidiInPort? port,
                TypedEventHandler<MidiInPort, MidiMessageReceivedEventArgs>? handler
            ) in inputs)
            {
                TryClose(() =>
                {
                    port.MessageReceived -= handler;
                    port.Dispose();
                });
            }

            TryClose(() => dawOutput?.Dispose());
            TryClose(() => performanceOutput?.Dispose());
            inputs.Clear();
            watchers.Clear();
            dawOutput = null;
            performanceOutput = null;
        }
    }

    private void RequestPadFrame()
    {
        activityDirty = true;

        if (!activityScheduled && !activityPumpRunning)
        {
            activityScheduled = true;
            _ = activityTimer.Change(PadFeedbackIntervalMilliseconds, Timeout.Infinite);
        }
    }

    private void PumpPadFeedback()
    {
        bool repaintMode;
        ControllerFeedback? queuedFeedback;

        lock (activityLock)
        {
            activityScheduled = false;

            if (activityDisposed || activityPumpRunning)
            {
                return;
            }

            activityPumpRunning = true;
            activityDirty = false;
            repaintMode = modeFeedbackRequested;
            modeFeedbackRequested = false;
            queuedFeedback = pendingFeedback;
            pendingFeedback = null;
        }

        bool failed = false;

        try
        {
            lock (portLock)
            {
                if (!disposed && enteredDawMode && dawOutput is not null)
                {
                    ControllerFeedback? target = queuedFeedback
                        ?? (repaintMode ? previousFeedback : null);

                    if (repaintMode)
                    {
                        previousFeedback = null;
                        Array.Fill(lastDisplayRows, null);
                        sessionLightsValid = false;
                    }

                    if (target is not null)
                    {
                        PaintFeedback(target);
                    }

                    PaintDrumPads(previousFeedback, false);
                }

                else
                {
                    // A performance-only connection cannot acknowledge LED frames. Discard its
                    // activity so an unpainted tap cannot keep the timer alive indefinitely.
                    lock (activityLock)
                    {
                        padHits.Clear();
                    }
                }
            }
        }

        catch (Exception exception)
        {
            failed = true;
            Trace.TraceError("MIDI feedback failed: {0}", exception.Message);
        }

        finally
        {
            lock (activityLock)
            {
                activityPumpRunning = false;

                if (
                    !failed
                    && !activityDisposed
                    && (activityDirty || padHits.HasPendingRelease(Environment.TickCount64))
                )
                {
                    activityScheduled = true;
                    _ = activityTimer.Change(PadFeedbackIntervalMilliseconds, Timeout.Infinite);
                }
            }
        }

        if (failed)
        {
            try
            {
                feedbackFailed(this);
            }

            catch (Exception exception)
            {
                Trace.TraceError("MIDI feedback recovery failed: {0}", exception.Message);
            }
        }
    }

    private void PaintDrumPads(ControllerFeedback? feedback, bool force)
    {
        bool drumMode = Volatile.Read(ref padMode) == 1;
        IReadOnlyList<int> notes = LaunchkeyMk3Profile.Model37.DrumPadLayout.Notes;
        Span<int> colors = stackalloc int[notes.Count];
        bool present = feedback is null || feedback.TrackNames.Count > 3;
        bool muted = feedback is not null
            && feedback.TrackMuted.Count > 3
            && feedback.TrackMuted[3];

        lock (activityLock)
        {
            if (activityDisposed)
            {
                return;
            }

            long now = Environment.TickCount64;

            for (int index = 0; index < notes.Count; index++)
            {
                _ = padHits.TryGetColor(notes[index], now, drumMode, muted, out int color);
                colors[index] = present ? color : 0;
            }
        }

        if (!drumMode)
        {
            return;
        }

        for (int index = 0; index < notes.Count; index++)
        {
            if (Volatile.Read(ref padMode) != 1)
            {
                return;
            }

            if (force || lastPadColors[index] != colors[index])
            {
                SendDaw(LaunchkeyMk3Protocol.SetDrumPad(notes[index], colors[index]));
                lastPadColors[index] = colors[index];
            }
        }

        lock (activityLock)
        {
            if (!activityDisposed)
            {
                long now = Environment.TickCount64;

                for (int index = 0; index < notes.Count; index++)
                {
                    padHits.MarkPainted(notes[index], colors[index] == 3, now);
                }
            }
        }
    }

    private void SendDaw(byte[] message)
    {
        dawOutput?.SendBuffer(message.AsBuffer());
    }

    private static void TryClose(Action close)
    {
        try
        {
            close();
        }

        catch (Exception exception)
        {
            Trace.TraceError("MIDI teardown failed: {0}", exception.Message);
        }
    }
}
