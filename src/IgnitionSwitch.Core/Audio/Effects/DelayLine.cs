namespace IgnitionSwitch.Core.Audio.Effects;

// Reset invalidates old storage in constant time; stale samples cannot enter a new tail.
internal sealed class DelayLine(int capacity)
{
    private readonly float[] samples = new float[capacity];
    private int write;
    private int valid;

    public int NonzeroCount { get; private set; }

    public float Read(int lag)
    {
        if (lag > valid)
        {
            return 0;
        }

        int index = write - lag;
        return samples[index < 0 ? index + samples.Length : index];
    }

    public float Read(float lag)
    {
        int whole = (int)lag;
        float fraction = lag - whole;
        float current = Read(whole);
        return fraction == 0 ? current : current + ((Read(whole + 1) - current) * fraction);
    }

    public void Write(float value)
    {
        if (valid == samples.Length && samples[write] != 0)
        {
            NonzeroCount--;
        }

        value = EffectMath.Flush(value);
        samples[write] = value;

        if (value != 0)
        {
            NonzeroCount++;
        }

        if (valid < samples.Length)
        {
            valid++;
        }

        write++;

        if (write == samples.Length)
        {
            write = 0;
        }
    }

    public void Reset()
    {
        write = 0;
        valid = 0;
        NonzeroCount = 0;
    }
}
