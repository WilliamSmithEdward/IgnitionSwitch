namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class StereoDelay
{
    private readonly StereoDelayLine line;
    private readonly CrossfadingTap tap;
    private readonly SmoothedCoefficient feedback;
    private readonly SmoothedCoefficient damping;
    private readonly int sampleRate;
    private float lastTime = 0.3f;
    private float lastFeedback = 0.3f;
    private float lastDamping = 0.3f;
    private float filteredLeft;
    private float filteredRight;
    private int tailFrames;

    public bool IsActive { get; private set; }

    public StereoDelay(int sampleRate)
    {
        this.sampleRate = sampleRate;
        line = new((sampleRate * 2) + 2);
        float initialLag = lastTime * sampleRate;
        tap = new(Math.Max(1, sampleRate / 50), initialLag);
        float smoothing = 1 - MathF.Exp(-1f / (sampleRate * 0.01f));
        feedback = new(lastFeedback, smoothing);
        damping = new(1 - (0.95f * lastDamping), smoothing);
    }

    public StereoSample Process(float left, float right, in DelayParameters parameters)
    {
        float time = EffectMath.Parameter(parameters.TimeSeconds, 0.04f, 2, 0.3f);
        float amount = EffectMath.Parameter(parameters.Feedback, 0, 0.85f, 0.3f);
        float dark = EffectMath.Parameter(parameters.Damping, 0, 1, 0.3f);
        float level = EffectMath.Parameter(parameters.ReturnLevel, 0, 1, 0.25f);

        if (time != lastTime)
        {
            lastTime = time;
            float lag = time * sampleRate;
            tap.SetTarget(lag, IsActive);
        }

        if (amount != lastFeedback)
        {
            lastFeedback = amount;
            feedback.SetTarget(amount);
        }

        if (dark != lastDamping)
        {
            lastDamping = dark;
            damping.SetTarget(1 - (0.95f * dark));
        }

        if (!IsActive)
        {
            feedback.Reset();
            damping.Reset();
        }

        bool input = left != 0 || right != 0;

        if (!input && !IsActive)
        {
            return default;
        }

        if (input)
        {
            IsActive = true;
            tailFrames = sampleRate * 300;
        }

        StereoSample delayed = tap.Read(line);
        float coefficient = damping.Next();
        float gain = feedback.Next();
        filteredLeft = EffectMath.Flush(
            filteredLeft + ((delayed.Left - filteredLeft) * coefficient)
        );
        filteredRight = EffectMath.Flush(
            filteredRight + ((delayed.Right - filteredRight) * coefficient)
        );
        line.Write(left + (filteredLeft * gain), right + (filteredRight * gain));

        if (
            !input
            && (--tailFrames <= 0 || (line.IsEmpty && filteredLeft == 0 && filteredRight == 0))
        )
        {
            Reset();
        }

        float outputLeft = delayed.Left * level;
        float outputRight = delayed.Right * level;
        return new(outputLeft, outputRight);
    }

    public void Reset()
    {
        line.Reset();
        tap.Reset();
        feedback.Reset();
        damping.Reset();
        filteredLeft = 0;
        filteredRight = 0;
        tailFrames = 0;
        IsActive = false;
    }
}
