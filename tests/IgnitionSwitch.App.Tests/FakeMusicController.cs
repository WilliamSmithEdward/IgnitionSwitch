using IgnitionSwitch.Midi.Contracts;

namespace IgnitionSwitch.App;

internal sealed class FakeMusicController : IMusicController
{
    public event EventHandler<MidiInputEvent>? InputReceived;
    public event EventHandler<MusicControlEvent>? ControlReceived;
    public event EventHandler<string>? StatusChanged;

    public bool IsConnected { get; private set; } = true;
    public string Status { get; private set; } = "Test controller";
    public ControllerIdentity? Identity { get; } = new("Test", "Independent controller", "1");
    public IReadOnlyList<MidiEndpointInfo> Endpoints { get; } = [];
    public bool IsDisposed { get; private set; }
    public List<ControllerFeedback> Feedback { get; } = [];

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        SetConnection(true);
    }

    public void Disconnect()
    {
        SetConnection(false);
    }

    public void SetConnection(bool connected)
    {
        IsConnected = connected;
        Status = connected ? "Test controller connected" : "Test controller disconnected";
        StatusChanged?.Invoke(this, Status);
    }

    public void UpdateFeedback(ControllerFeedback feedback)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Feedback.Add(feedback);
    }

    public void EmitInput(MidiInputEvent input)
    {
        InputReceived?.Invoke(this, input);
    }

    public void EmitControl(MusicControlEvent control)
    {
        ControlReceived?.Invoke(this, control);
    }

    public Action<MidiInputEvent> CaptureInputCallback()
    {
        EventHandler<MidiInputEvent>? captured = InputReceived;
        return input => captured?.Invoke(this, input);
    }

    public Action<MusicControlEvent> CaptureControlCallback()
    {
        EventHandler<MusicControlEvent>? captured = ControlReceived;
        return control => captured?.Invoke(this, control);
    }

    public Action CaptureStatusCallback()
    {
        EventHandler<string>? captured = StatusChanged;
        return () => captured?.Invoke(this, Status);
    }

    public void Dispose()
    {
        IsDisposed = true;
        IsConnected = false;
    }
}
