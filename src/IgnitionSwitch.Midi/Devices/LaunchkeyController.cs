using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using IgnitionSwitch.Midi.Contracts;
using IgnitionSwitch.Midi.Profiles;
using IgnitionSwitch.Midi.Protocol;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Storage.Streams;

namespace IgnitionSwitch.Midi.Devices;

/// <summary>
/// Connects a verified Launchkey 37 MK3 through Windows MIDI. "DAW mode" is the hardware control
/// protocol; IgnitionSwitch remains a standalone instrument and does not require another music app.
/// </summary>
public sealed class LaunchkeyController : IMusicController
{
    private const string ContainerProperty = "System.Devices.ContainerId";
    private readonly Lock stateLock = new();
    private ControllerSession? session;
    private bool connected;
    private bool disposed;

    /// <inheritdoc />
    public event EventHandler<MidiInputEvent>? InputReceived;

    /// <inheritdoc />
    public event EventHandler<MusicControlEvent>? ControlReceived;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (stateLock)
            {
                return connected;
            }
        }
    }

    /// <inheritdoc />
    public string Status
    {
        get
        {
            lock (stateLock)
            {
                return field;
            }
        }

        private set;
    } = "Connect your Launchkey 37 MK3 by USB, then choose Connect.";

    /// <inheritdoc />
    public ControllerIdentity? Identity
    {
        get
        {
            lock (stateLock)
            {
                return field;
            }
        }

        private set;
    }

    /// <inheritdoc />
    public IReadOnlyList<MidiEndpointInfo> Endpoints
    {
        get
        {
            lock (stateLock)
            {
                return field;
            }
        }

        private set;
    } = Array.Empty<MidiEndpointInfo>();

    /// <inheritdoc />
    public ControllerPadLayout? DrumPadLayout => LaunchkeyMk3Profile.Model37.DrumPadLayout;

    /// <inheritdoc />
    public void Connect()
    {
        ControllerSession next;

        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (session is not null)
            {
                return;
            }

            next = new(OnFeedbackFailed);
            session = next;
        }

        SetStatus(next, "Looking for your Launchkey 37 MK3…");
        _ = Task.Run(() => ConnectAsync(next));
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        ReleaseSession(null, "Controller disconnected. Choose Connect to reconnect.");
    }

    /// <inheritdoc />
    public void UpdateFeedback(ControllerFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(feedback.TrackNames);
        ArgumentNullException.ThrowIfNull(feedback.TrackMuted);

        if (
            feedback.TrackNames.Count is < 1
                or > 8
                    || feedback.SelectedTrack < 0
                    || feedback.SelectedTrack >= feedback.TrackNames.Count
                    || feedback.TrackNames.Count != feedback.TrackMuted.Count
        )
        {
            return;
        }

        ControllerSession? current;

        lock (stateLock)
        {
            current = connected && !disposed ? session : null;
        }

        if (current is null)
        {
            return;
        }

        // Copy caller-owned data before crossing threads or touching hardware.
        ControllerFeedback snapshot = LaunchkeyFeedbackPresentation.CreateSnapshot(feedback);

        if (
            snapshot.TrackNames.Count is < 1
                or > 8
                    || snapshot.TrackNames.Count != snapshot.TrackMuted.Count
                    || feedback.SelectedTrack >= snapshot.TrackNames.Count
        )
        {
            return;
        }

        current.WriteFeedback(snapshot);
    }

    /// <inheritdoc />
    public void UpdatePadFeedback(PadFeedback feedback)
    {
        ControllerSession? current;

        lock (stateLock)
        {
            current = connected && !disposed ? session : null;
        }

        current?.UpdatePadFeedback(feedback);
    }

    private void OnFeedbackFailed(ControllerSession current)
    {
        ReleaseSession(
            current,
            "Launchkey feedback stopped. Check the USB cable, close other MIDI apps, then reconnect."
        );
    }

    /// <summary>Disconnects and prevents future connection attempts.</summary>
    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        ReleaseSession(null, "Controller closed.");
    }

    private async Task ConnectAsync(ControllerSession current)
    {
        try
        {
            DeviceInformationCollection inputDevices = await DeviceInformation.FindAllAsync(
                MidiInPort.GetDeviceSelector(),
                [ContainerProperty]
            );
            DeviceInformationCollection outputDevices = await DeviceInformation.FindAllAsync(
                MidiOutPort.GetDeviceSelector(),
                [ContainerProperty]
            );
            MidiEndpointInfo[] inventory =
            [
                .. inputDevices.Select(device => Describe(device, MidiEndpointDirection.Input)),
                .. outputDevices.Select(device => Describe(device, MidiEndpointDirection.Output))
            ];

            lock (stateLock)
            {
                if (session != current)
                {
                    return;
                }

                Endpoints = Array.AsReadOnly(inventory);
            }

            MidiEndpointInfo[] candidates =
            [
                .. inventory.Where(endpoint => endpoint.IsLaunchkeyCandidate)
            ];

            if (candidates.Length == 0)
            {
                ReleaseSession(
                    current,
                    "No Launchkey MIDI ports found. Connect it by USB and check its Windows driver, then retry."
                );
                return;
            }

            if (!LaunchkeyEndpointMatcher.TryMatch(inventory, out LaunchkeyEndpointSet? ports))
            {
                ReleaseSession(
                    current,
                    "Launchkey ports are ambiguous. Connect one Launchkey, close other MIDI apps, then retry."
                );
                return;
            }

            SetStatus(current, "Checking Launchkey model and firmware…");
            await OpenInputAsync(current, ports!.PerformanceInput, true);
            await OpenOutputAsync(current, ports.PerformanceOutput, true);
            current.SendPerformance(LaunchkeyMk3Protocol.IdentityInquiry());

            ControllerIdentity verified = await current.IdentityReply.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                current.CancellationToken
            );

            if (ports.DawInput is not null && ports.DawOutput is not null)
            {
                await OpenInputAsync(current, ports.DawInput, false);
                await OpenOutputAsync(current, ports.DawOutput, false);
                current.EnterDawMode();
            }

            lock (stateLock)
            {
                if (session != current)
                {
                    return;
                }

                Identity = verified;
                connected = true;
            }

            current.StartWatchers(
                () => ReleaseSession(
                    current,
                    "Launchkey unplugged. Reconnect the USB cable, then choose Connect."
                )
            );
            SetStatus(
                current,
                current.HasDawOutput
                    ? $"{verified.Model} connected · firmware {verified.Firmware} · keys and controls ready"
                    : $"{verified.Model} connected · keys ready. DAW-control ports missing; reconnect USB to enable pads and feedback."
            );
        }

        catch (OperationCanceledException)
        {
            // Disconnect owns teardown and its status; an obsolete connection must not overwrite
            // either.
        }

        catch (TimeoutException)
        {
            ReleaseSession(
                current,
                "Could not verify a Launchkey 37 MK3. Close other MIDI apps, check the model and USB connection, then retry."
            );
        }

        catch (Exception exception)
        {
            // WinRT discovery and driver failures must not escape the background connection task.
            Trace.TraceError("MIDI connection failed: {0}", exception.Message);
            ReleaseSession(
                current,
                "Could not open Launchkey ports. Close other MIDI apps, check its Windows driver, then reconnect."
            );
        }
    }

    private async Task OpenInputAsync(
        ControllerSession current,
        MidiEndpointInfo endpoint,
        bool performance
    )
    {
        current.CancellationToken.ThrowIfCancellationRequested();
        MidiInPort port = await MidiInPort.FromIdAsync(endpoint.Id)
            ?? throw new InvalidOperationException("Windows could not open the MIDI input.");
        current.AttachInput(port, (sender, args) => Receive(current, args, performance));
    }

    private static async Task OpenOutputAsync(
        ControllerSession current,
        MidiEndpointInfo endpoint,
        bool performance
    )
    {
        current.CancellationToken.ThrowIfCancellationRequested();
        IMidiOutPort port = await MidiOutPort.FromIdAsync(endpoint.Id)
            ?? throw new InvalidOperationException("Windows could not open the MIDI output.");
        current.AttachOutput(port, performance);
    }

    private void Receive(
        ControllerSession current,
        MidiMessageReceivedEventArgs args,
        bool performance
    )
    {
        try
        {
            lock (stateLock)
            {
                if (session != current)
                {
                    return;
                }
            }

            IBuffer rawData = args.Message.RawData;

            if (rawData.Length is < 1 or > 64)
            {
                return;
            }

            byte[] bytes = rawData.ToArray();

            if (
                performance
                && LaunchkeyMk3Protocol.TryReadIdentity(bytes, out ControllerIdentity? verified)
            )
            {
                _ = current.IdentityReply.TrySetResult(verified!);
                return;
            }

            if (
                !IsConnected
                || !MidiMessageParser.TryParse(bytes, performance, out MidiInputEvent? input)
            )
            {
                return;
            }

            MidiInputEvent routed = input!;

            if (
                !performance
                && current.TryMapControl(input!, out MusicControlEvent? control, out routed)
            )
            {
                RaiseEvent(ControlReceived, control!, "Music control");
            }

            RaiseEvent(InputReceived, routed, "MIDI input");
        }

        catch (Exception exception)
        {
            // Native MIDI callbacks must never propagate exceptions back into the Windows driver.
            Trace.TraceError("MIDI callback failed: {0}", exception.Message);
        }
    }

    private static MidiEndpointInfo Describe(
        DeviceInformation device,
        MidiEndpointDirection direction
    )
    {
        _ = device.Properties.TryGetValue(ContainerProperty, out object? container);
        string? containerId = container?.ToString();

        if (string.IsNullOrWhiteSpace(containerId) || containerId == Guid.Empty.ToString())
        {
            containerId = null;
        }

        return new(
            device.Id,
            device.Name,
            direction,
            containerId,
            LaunchkeyMk3Profile.IsCandidateName(device.Name),
            LaunchkeyMk3Profile.IsDawName(device.Name)
        );
    }

    private void SetStatus(ControllerSession current, string message)
    {
        lock (stateLock)
        {
            if (session != current)
            {
                return;
            }

            Status = message;
        }

        RaiseEvent(StatusChanged, message, "MIDI status");
    }

    private void ReleaseSession(ControllerSession? expected, string message)
    {
        ControllerSession? previous;

        lock (stateLock)
        {
            if (expected is not null && session != expected)
            {
                return;
            }

            previous = session;
            session = null;
            Identity = null;
            connected = false;
            Status = message;
        }

        previous?.Dispose();
        RaiseEvent(StatusChanged, message, "MIDI status");
    }

    private void RaiseEvent<T>(EventHandler<T>? handlers, T value, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)subscriber)(this, value);
            }

            catch (Exception exception)
            {
                Trace.TraceError("{0} subscriber failed: {1}", eventName, exception.Message);
            }
        }
    }
}
