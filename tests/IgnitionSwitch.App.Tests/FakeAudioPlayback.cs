using IgnitionSwitch.App.Audio;

namespace IgnitionSwitch.App;

internal sealed class FakeAudioPlayback : IAudioPlayback
{
    public string Status => "Test audio output";
    public bool IsRunning { get; private set; } = true;
    public bool IsDisposed { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsRunning = true;
    }

    public void EnsureRunning()
    {
        if (!IsRunning)
        {
            Start();
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        IsRunning = false;
    }
}
