using System.Diagnostics;
using System.Runtime.InteropServices;
using IgnitionSwitch.Core.Sequencing;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace IgnitionSwitch.App.Audio;

internal sealed partial class AudioPlayback(WorkstationEngine engine) : IAudioPlayback
{
    private WasapiOut? output;
    private bool disposed;
    private long lastStart;
    private volatile bool playbackFailed;
    private string? outputDeviceId;
    private string? compatibilityDeviceId;

    public string Status { get; private set; } = "Audio is starting…";
    public bool IsRunning => !playbackFailed && output?.PlaybackState == PlaybackState.Playing;
    public int RequestedBufferMilliseconds { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lastStart = Environment.TickCount64;
        DisposeOutput();
        playbackFailed = false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia
            );
            outputDeviceId = device.ID;
            int preferred = PreferredBufferMilliseconds(device);

            if (StringComparer.Ordinal.Equals(compatibilityDeviceId, outputDeviceId))
            {
                preferred = SharedAudioBufferPolicy.CompatibilityBufferMilliseconds;
            }

            try
            {
                output = InitializeOutput(device, preferred);
                RequestedBufferMilliseconds = preferred;
            }

            catch (COMException exception) when (
                preferred < SharedAudioBufferPolicy.CompatibilityBufferMilliseconds
            )
            {
                Trace.TraceWarning(
                    "Preferred shared audio buffer was rejected: {0}",
                    exception.Message
                );
                output = InitializeOutput(
                    device,
                    SharedAudioBufferPolicy.CompatibilityBufferMilliseconds
                );
                compatibilityDeviceId = outputDeviceId;
                RequestedBufferMilliseconds =
                    SharedAudioBufferPolicy.CompatibilityBufferMilliseconds;
            }

            output.PlaybackStopped += OnPlaybackStopped;
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
            playbackFailed = true;
            compatibilityDeviceId = args.Exception is COMException
            {
                ErrorCode: AudioClientErrorCode.DeviceInvalidated
                    or AudioClientErrorCode.ResourcesInvalidated
                    or AudioClientErrorCode.ServiceNotRunning
            }
                ? null
                : outputDeviceId;
            Trace.TraceWarning("Audio playback stopped: {0}", args.Exception.Message);
            SetStatus("Audio output changed · reconnecting…");
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
    }

    private static int PreferredBufferMilliseconds(MMDevice device)
    {
        try
        {
            using AudioClient client = device.AudioClient;
            return SharedAudioBufferPolicy.PreferredMilliseconds(client.DefaultDevicePeriod);
        }

        catch (COMException exception)
        {
            Trace.TraceWarning("Could not query the shared audio period: {0}", exception.Message);
            return SharedAudioBufferPolicy.PreferredMilliseconds(0);
        }
    }

    private WasapiOut InitializeOutput(MMDevice device, int requestedMilliseconds)
    {
        var candidate = new WasapiOut(
            device,
            AudioClientShareMode.Shared,
            true,
            requestedMilliseconds
        );

        try
        {
            candidate.Init(new EngineSampleProvider(engine));
            return candidate;
        }

        catch
        {
            candidate.Dispose();
            throw;
        }
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
