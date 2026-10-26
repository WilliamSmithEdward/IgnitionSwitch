using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace IgnitionSwitch.App.Audio;

internal sealed partial class WasapiOutput : IDisposable
{
    private readonly WasapiOut output;
    private readonly AudioThreadRegistration registration = new();
    private readonly ManualResetEventSlim stopped = new(true);
    private readonly SynchronizationContext? consumerContext;
    private volatile bool disposed;
    private int completed = 1;
    private int started;

    public WasapiOutput(MMDevice device, AudioClientShareMode shareMode, int milliseconds)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        consumerContext = previous;

        try
        {
            // NAudio 2.2.1 captures this context in its constructor. Its finally callback must
            // remain on the render thread so MMCSS can be reverted before thread termination.
            SynchronizationContext.SetSynchronizationContext(null);
            output = new WasapiOut(device, shareMode, true, milliseconds);
        }

        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        output.PlaybackStopped += OnPlaybackStopped;
    }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public PlaybackState PlaybackState =>
        Volatile.Read(ref completed) != 0 ? PlaybackState.Stopped : output.PlaybackState;
    public WaveFormat OutputWaveFormat => output.OutputWaveFormat;

    public void Init(IWaveProvider provider)
    {
        output.Init(new ScheduledWaveProvider(provider, registration));
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException(
                "Each output instance supports one playback lifetime."
            );
        }

        stopped.Reset();
        Volatile.Write(ref completed, 0);

        try
        {
            output.Play();
        }

        catch
        {
            Volatile.Write(ref completed, 1);
            stopped.Set();
            throw;
        }
    }

    public void Stop()
    {
        output.Stop();
        stopped.Wait();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            Stop();
        }

        finally
        {
            // Keep our cleanup handler installed through Stop/Join, even after consumers detach.
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
            stopped.Dispose();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        try
        {
            registration.Leave();
        }

        finally
        {
            Volatile.Write(ref completed, 1);
            stopped.Set();
        }

        // Never touch the gate after publication. Consumers run away from the native render
        // callback, so their Stop/Dispose cannot self-join NAudio's exceptional-exit thread.
        if (consumerContext is not null)
        {
            try
            {
                consumerContext.Post(_ => NotifyStopped(args), null);
                return;
            }

            catch (InvalidOperationException exception)
            {
                Trace.TraceWarning("Audio notification context closed: {0}", exception.Message);
            }
        }

        _ = ThreadPool.QueueUserWorkItem(_ => NotifyStopped(args));
    }

    private void NotifyStopped(StoppedEventArgs args)
    {
        if (!disposed)
        {
            PlaybackStopped?.Invoke(this, args);
        }
    }

    private sealed class ScheduledWaveProvider(
        IWaveProvider source,
        AudioThreadRegistration registration
    ) : IWaveProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            registration.Enter();
            return source.Read(buffer, offset, count);
        }
    }
}
