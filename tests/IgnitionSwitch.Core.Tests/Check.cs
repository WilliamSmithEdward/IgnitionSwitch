using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Near(double expected, double actual, double tolerance, string message)
    {
        That(
            Math.Abs(expected - actual) <= tolerance,
            $"{message}: expected {expected}, got {actual}"
        );
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }

        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }

        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static float[] Render(WorkstationEngine engine, double seconds)
    {
        float[] audio = new float[(int)Math.Round(seconds * WorkstationEngine.SampleRate) * 2];
        engine.Render(audio, 0, audio.Length);
        return audio;
    }

    public static double Energy(float[] audio)
    {
        return audio.Sum(value => Math.Abs((double)value));
    }

    public static SessionSnapshot WithNotes(
        WorkstationEngine engine,
        int track,
        params LoopNote[] notes
    )
    {
        SessionSnapshot session = engine.CaptureSession();
        TrackSnapshot[] tracks = [.. session.Tracks];
        tracks[track] = tracks[track] with { Notes = notes };
        return session with { Tracks = tracks };
    }
}
