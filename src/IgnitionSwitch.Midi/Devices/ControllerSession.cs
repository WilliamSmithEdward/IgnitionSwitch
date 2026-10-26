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
    private readonly Lock portLock = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<(
        MidiInPort Port,
        TypedEventHandler<MidiInPort, MidiMessageReceivedEventArgs> Handler
    )> inputs = [];
    private readonly List<DeviceWatcher> watchers = [];
    private IMidiOutPort? performanceOutput;
    private IMidiOutPort? dawOutput;
    private ControllerFeedback? previousFeedback;
    private bool enteredDawMode;
    private bool disposed;
    private readonly LaunchkeyControlMapper mapper = new();

    public CancellationToken CancellationToken { get; }

    public ControllerSession()
    {
        CancellationToken = cancellation.Token;
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
            string[] names = [
                "Keys volume", "Bass volume", "Pad volume", "Drums volume", "Master", "Tempo",
                "Unassigned", "Unassigned"
            ];

            for (int knob = 0; knob < names.Length; knob++)
            {
                SendDaw(LaunchkeyMk3Protocol.SetKnobName(knob, names[knob]));
            }

            SendDaw(LaunchkeyMk3Protocol.SetDisplayRow(0, "IgnitionSwitch"));
            SendDaw(LaunchkeyMk3Protocol.SetDisplayRow(1, "Ready to play"));
        }
    }

    public bool TryMapControl(MidiInputEvent input, out MusicControlEvent? control)
    {
        lock (portLock)
        {
            int oldPadMode = mapper.PadMode;
            bool mapped = mapper.TryMap(input, out control);

            if (
                oldPadMode != mapper.PadMode
                && mapper.PadMode == 2
                && previousFeedback is { } cached
            )
            {
                previousFeedback = null;
                WriteFeedback(cached);
            }

            return mapped;
        }
    }

    public void WriteFeedback(ControllerFeedback feedback)
    {
        lock (portLock)
        {
            if (disposed || !enteredDawMode || dawOutput is null)
            {
                return;
            }

            if (
                previousFeedback is { } previous
                && previous.SelectedTrack == feedback.SelectedTrack
                && previous.Playing == feedback.Playing
                && previous.Recording == feedback.Recording
                && previous.TrackNames.SequenceEqual(feedback.TrackNames)
                && previous.TrackMuted.SequenceEqual(feedback.TrackMuted)
            )
            {
                return;
            }

            if (mapper.PadMode == 2)
            {
                for (int pad = 0; pad < 16; pad++)
                {
                    int track = pad % 8;
                    bool present = track < feedback.TrackNames.Count;
                    int color = !present
                        ? 0
                        : track == feedback.SelectedTrack ? feedback.Recording ? 5 : 45 : 13;

                    if (pad >= 8)
                    {
                        color = !present ? 0 : feedback.TrackMuted[track] ? 5 : 19;
                    }

                    SendDaw(LaunchkeyMk3Protocol.SetSessionPad(pad, color));
                }
            }

            SendDaw(
                LaunchkeyMk3Protocol.SetDisplayRow(0, feedback.TrackNames[feedback.SelectedTrack])
            );
            SendDaw(
                LaunchkeyMk3Protocol.SetDisplayRow(
                    1,
                    feedback.Recording
                        ? "Recording"
                        : feedback.Playing ? "Playing" : "Ready to play"
                )
            );
            previousFeedback = feedback;
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
