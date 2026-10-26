using System.Diagnostics;
using System.Runtime.InteropServices;
using IgnitionSwitch.Core.Sequencing;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IgnitionSwitch.App.Audio;

internal sealed partial class AudioPlayback(WorkstationEngine engine) : IAudioPlayback
{
    private volatile WasapiOutput? output;
    private volatile bool disposed;
    private long lastStart;
    private volatile bool playbackFailed;
    private string? outputDeviceId;
    private string? compatibilityDeviceId;
    private string? exclusiveFallbackDeviceId;
    private AudioOutputMode activeMode;

    public string Status { get; private set; } = "Audio is starting…";
    public bool IsRunning => !playbackFailed && output?.PlaybackState == PlaybackState.Playing;
    public int RequestedBufferMilliseconds { get; private set; }

    public AudioOutputMode Mode
    {
        get;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
            exclusiveFallbackDeviceId = null;
        }
    }

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
            bool exclusiveFallback = Mode == AudioOutputMode.ExclusiveLowLatency;

            if (
                Mode == AudioOutputMode.ExclusiveLowLatency
                && !StringComparer.Ordinal.Equals(exclusiveFallbackDeviceId, outputDeviceId)
            )
            {
                try
                {
                    output = InitializeExclusiveOutput(device);
                    activeMode = AudioOutputMode.ExclusiveLowLatency;
                    exclusiveFallback = false;
                }

                catch (Exception exception) when (IsOutputException(exception))
                {
                    Trace.TraceWarning(
                        "Exclusive audio unavailable; using shared output: {0}",
                        exception.Message
                    );
                }
            }

            if (output is null)
            {
                output = InitializeSharedOutput(device);
                activeMode = AudioOutputMode.Shared;
            }

            output.PlaybackStopped += OnPlaybackStopped;
            string selectedMode = activeMode == AudioOutputMode.ExclusiveLowLatency
                ? "Exclusive low latency"
                : "Shared";
            string fallback = exclusiveFallback
                ? "Low-latency mode unavailable; shared audio used. Close other audio apps or use Shared. "
                : string.Empty;
            SetStatus(
                $"{fallback}Audio · {device.FriendlyName} · {selectedMode} · requested {RequestedBufferMilliseconds} ms"
            );
            output.Play();
        }

        catch (Exception exception) when (IsOutputException(exception))
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
            bool deviceChanged = args.Exception is COMException
            {
                ErrorCode: AudioClientErrorCode.DeviceInvalidated
                    or AudioClientErrorCode.ResourcesInvalidated
                    or AudioClientErrorCode.ServiceNotRunning
            };
            compatibilityDeviceId = activeMode == AudioOutputMode.Shared && !deviceChanged
                ? outputDeviceId
                : null;
            exclusiveFallbackDeviceId = activeMode == AudioOutputMode.ExclusiveLowLatency
                && !deviceChanged
                ? outputDeviceId
                : null;
            Trace.TraceWarning("Audio playback stopped: {0}", args.Exception.Message);
            SetStatus(
                exclusiveFallbackDeviceId is null
                    ? "Audio output changed · reconnecting…"
                    : "Low-latency output stopped · reconnecting with shared audio…"
            );
            playbackFailed = true;
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

    private WasapiOutput InitializeSharedOutput(MMDevice device)
    {
        int preferred = StringComparer.Ordinal.Equals(compatibilityDeviceId, outputDeviceId)
            ? SharedAudioBufferPolicy.CompatibilityBufferMilliseconds
            : PreferredBufferMilliseconds(device);

        try
        {
            WasapiOutput candidate = InitializeOutput(
                device,
                AudioClientShareMode.Shared,
                preferred,
                new SampleToWaveProvider(new EngineSampleProvider(engine))
            );
            RequestedBufferMilliseconds = preferred;
            return candidate;
        }

        catch (COMException exception) when (
            preferred < SharedAudioBufferPolicy.CompatibilityBufferMilliseconds
        )
        {
            Trace.TraceWarning(
                "Preferred shared audio buffer was rejected: {0}",
                exception.Message
            );
            WasapiOutput candidate = InitializeOutput(
                device,
                AudioClientShareMode.Shared,
                SharedAudioBufferPolicy.CompatibilityBufferMilliseconds,
                new SampleToWaveProvider(new EngineSampleProvider(engine))
            );
            compatibilityDeviceId = outputDeviceId;
            RequestedBufferMilliseconds = SharedAudioBufferPolicy.CompatibilityBufferMilliseconds;
            return candidate;
        }
    }

    private WasapiOutput InitializeExclusiveOutput(MMDevice device)
    {
        using AudioClient client = device.AudioClient;
        int milliseconds = ExclusiveAudioBufferPolicy.PreferredMilliseconds(
            client.MinimumDevicePeriod
        );
        var source = new EngineSampleProvider(engine);
        IWaveProvider provider;

        if (client.IsFormatSupported(AudioClientShareMode.Exclusive, source.WaveFormat))
        {
            provider = new SampleToWaveProvider(source);
        }

        else
        {
            var pcm = new SampleToWaveProvider16(source);

            if (!client.IsFormatSupported(AudioClientShareMode.Exclusive, pcm.WaveFormat))
            {
                throw new NotSupportedException(
                    "The output does not support 48 kHz stereo float or PCM16 in exclusive mode."
                );
            }

            provider = pcm;
        }

        WasapiOutput candidate = InitializeOutput(
            device,
            AudioClientShareMode.Exclusive,
            milliseconds,
            provider
        );

        // Pinned NAudio 2.2.1 replaces this reference before enabling its exclusive DMO fallback.
        // Reject that path before playback even if the replacement describes an equivalent format.
        if (!ReferenceEquals(candidate.OutputWaveFormat, provider.WaveFormat))
        {
            candidate.Dispose();
            throw new NotSupportedException(
                "Exclusive output changed the validated native format."
            );
        }

        RequestedBufferMilliseconds = milliseconds;
        return candidate;
    }

    private static WasapiOutput InitializeOutput(
        MMDevice device,
        AudioClientShareMode shareMode,
        int requestedMilliseconds,
        IWaveProvider provider
    )
    {
        var candidate = new WasapiOutput(device, shareMode, requestedMilliseconds);

        try
        {
            candidate.Init(provider);
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

        WasapiOutput previous = output;
        output = null;
        previous.PlaybackStopped -= OnPlaybackStopped;
        previous.Stop();
        previous.Dispose();
    }

    private static bool IsOutputException(Exception exception)
    {
        return exception is COMException or InvalidOperationException or NotSupportedException;
    }
}
