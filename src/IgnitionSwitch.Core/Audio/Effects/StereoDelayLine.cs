namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class StereoDelayLine(int capacity)
{
    private readonly DelayLine left = new(capacity);
    private readonly DelayLine right = new(capacity);

    public bool IsEmpty => left.NonzeroCount == 0 && right.NonzeroCount == 0;

    public StereoSample Read(float lag)
    {
        return new(left.Read(lag), right.Read(lag));
    }

    public void Write(float inputLeft, float inputRight)
    {
        left.Write(inputLeft);
        right.Write(inputRight);
    }

    public void Reset()
    {
        left.Reset();
        right.Reset();
    }
}
