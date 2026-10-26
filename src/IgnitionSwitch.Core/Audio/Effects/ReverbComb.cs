namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class ReverbComb
{
    private readonly DelayLine line;
    private readonly int lag;
    private readonly SmoothedCoefficient feedback;
    private float filtered;

    public ReverbComb(int lag, int sampleRate, float smoothing)
    {
        this.lag = lag;
        line = new(lag);
        feedback = new(DecayGain(1.5f, sampleRate), smoothing);
    }

    public bool IsEmpty => line.NonzeroCount == 0 && filtered == 0;

    public void SetDecay(float seconds, int sampleRate, bool active)
    {
        feedback.SetTarget(DecayGain(seconds, sampleRate));

        if (!active)
        {
            feedback.Reset();
        }
    }

    public float Process(float input, float damping)
    {
        float delayed = line.Read(lag);
        filtered = EffectMath.Flush(filtered + ((delayed - filtered) * damping));
        line.Write(input + (filtered * feedback.Next()));
        return delayed;
    }

    public void Reset()
    {
        line.Reset();
        feedback.Reset();
        filtered = 0;
    }

    private float DecayGain(float seconds, int sampleRate)
    {
        // A loop attenuates by 60 dB after the requested T60 interval at low frequencies.
        return MathF.Exp(-6.90775528f * lag / (sampleRate * seconds));
    }
}
