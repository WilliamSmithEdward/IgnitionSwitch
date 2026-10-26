using System.Diagnostics;
using System.Runtime.InteropServices;
using IgnitionSwitch.Core.Sequencing;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace IgnitionSwitch.App.Audio;

internal sealed partial class AudioPlayback(WorkstationEngine engine) : IAudioPlayback
{
    private WasapiOut? output;
    private bool disposed;
    private long lastStart;

    public string Status { get; private set; } = "Audio is starting…";
    public bool IsRunning => output?.PlaybackState == PlaybackState.Playing;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lastStart = Environment.TickCount64;
        DisposeOutput();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia
            );
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(new EngineSampleProvider(engine));
            output.Play();
            SetStatus($"Audio · {device.FriendlyName}");
        }

        catch (Exception exception) when (
            exception is COMException
                or InvalidOperationException
                or NotSupportedException
        )
        {
            DisposeOutput();
            Trace.TraceWarning("Audio initialization failed: {0}", exception.Message);
            SetStatus("Audio unavailable · connect speakers or headphones in Windows. Retrying…");
        }
    }

    public void EnsureRunning()
    {
        if (!disposed && !IsRunning && Environment.TickCount64 - lastStart >= 3000)
        {
            Start();
        }
    }

    public void Dispose()
    {
        disposed = true;
        DisposeOutput();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (!disposed && ReferenceEquals(sender, output) && args.Exception is not null)
        {
            Trace.TraceWarning("Audio playback stopped: {0}", args.Exception.Message);
            SetStatus("Audio output changed · reconnecting…");
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
    }

    private void DisposeOutput()
    {
        if (output is null)
        {
            return;
        }

        WasapiOut previous = output;
        output = null;
        previous.PlaybackStopped -= OnPlaybackStopped;
        previous.Stop();
        previous.Dispose();
    }
}
