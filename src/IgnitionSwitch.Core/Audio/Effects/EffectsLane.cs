namespace IgnitionSwitch.Core.Audio.Effects;

// Separate delay and room inputs remain parallel; neither effect feeds the other.
internal sealed class EffectsLane
{
    private readonly StereoDelay delay;
    private readonly StereoReverb room;

    public bool IsActive => delay.IsActive || room.IsActive;

    public EffectsLane(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleRate, 192000);
        delay = new(sampleRate);
        room = new(sampleRate);
    }

    public StereoSample Process(
        float delayLeft,
        float delayRight,
        float roomLeft,
        float roomRight,
        in DelayParameters delay,
        in ReverbParameters reverb
    )
    {
        StereoSample echo = this.delay.Process(
            EffectMath.Input(delayLeft),
            EffectMath.Input(delayRight),
            delay
        );
        StereoSample space = room.Process(
            EffectMath.Input(roomLeft),
            EffectMath.Input(roomRight),
            reverb
        );
        return new(
            EffectMath.Flush(echo.Left + space.Left),
            EffectMath.Flush(echo.Right + space.Right)
        );
    }

    public void Reset()
    {
        delay.Reset();
        room.Reset();
    }
}
