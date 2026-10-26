namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class ReverbAllPass(int lag)
{
    private readonly DelayLine line = new(lag);

    public bool IsEmpty => line.NonzeroCount == 0;

    public float Process(float input)
    {
        float delayed = line.Read(lag);
        float output = EffectMath.Flush(delayed - (input * 0.5f));
        line.Write(input + (output * 0.5f));
        return output;
    }

    public void Reset()
    {
        line.Reset();
    }
}
