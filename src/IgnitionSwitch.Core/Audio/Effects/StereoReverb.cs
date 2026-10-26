namespace IgnitionSwitch.Core.Audio.Effects;

internal sealed class StereoReverb
{
    private static readonly int[] CombLengths = [1116, 1188, 1277, 1356];
    private static readonly int[] DiffuserLengths = [556, 441, 341];
    private readonly StereoDelayLine preDelay;
    private readonly CrossfadingTap preDelayTap;
    private readonly ReverbComb[] leftCombs = new ReverbComb[4];
    private readonly ReverbComb[] rightCombs = new ReverbComb[4];
    private readonly ReverbAllPass[] leftDiffusers = new ReverbAllPass[3];
    private readonly ReverbAllPass[] rightDiffusers = new ReverbAllPass[3];
    private readonly SmoothedCoefficient damping;
    private readonly int sampleRate;
    private float lastDecay = 1.5f;
    private float lastDamping = 0.3f;
    private float lastPreDelay = 0.02f;
    private int tailFrames;

    public bool IsActive { get; private set; }

    public StereoReverb(int sampleRate)
    {
        this.sampleRate = sampleRate;
        preDelay = new((sampleRate / 4) + 3);
        float initialLag = lastPreDelay * sampleRate;
        preDelayTap = new(Math.Max(1, sampleRate / 50), initialLag);
        float smoothing = 1 - MathF.Exp(-1f / (sampleRate * 0.01f));
        damping = new(1 - (0.9f * lastDamping), smoothing);

        for (int index = 0; index < leftCombs.Length; index++)
        {
            leftCombs[index] = new(Scale(CombLengths[index], sampleRate), sampleRate, smoothing);
            rightCombs[index] = new(
                Scale(CombLengths[index] + 23, sampleRate),
                sampleRate,
                smoothing
            );
        }

        for (int index = 0; index < leftDiffusers.Length; index++)
        {
            leftDiffusers[index] = new(Scale(DiffuserLengths[index], sampleRate));
            rightDiffusers[index] = new(Scale(DiffuserLengths[index] + 23, sampleRate));
        }
    }

    public StereoSample Process(float left, float right, in ReverbParameters parameters)
    {
        float decay = EffectMath.Parameter(parameters.DecaySeconds, 0.2f, 8, 1.5f);
        float dark = EffectMath.Parameter(parameters.Damping, 0, 1, 0.3f);
        float delay = EffectMath.Parameter(parameters.PreDelaySeconds, 0, 0.25f, 0.02f);
        float level = EffectMath.Parameter(parameters.ReturnLevel, 0, 1, 0.25f);

        if (decay != lastDecay)
        {
            lastDecay = decay;

            for (int index = 0; index < leftCombs.Length; index++)
            {
                leftCombs[index].SetDecay(decay, sampleRate, IsActive);
                rightCombs[index].SetDecay(decay, sampleRate, IsActive);
            }
        }

        if (dark != lastDamping)
        {
            lastDamping = dark;
            damping.SetTarget(1 - (0.9f * dark));
        }

        if (delay != lastPreDelay)
        {
            lastPreDelay = delay;
            float lag = delay * sampleRate;
            preDelayTap.SetTarget(lag, IsActive);
        }

        if (!IsActive)
        {
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
            tailFrames = sampleRate * 45;
        }

        preDelay.Write(left, right);
        StereoSample delayed = preDelayTap.Read(preDelay, 1);
        float inputLeft = (delayed.Left + (delayed.Right * 0.35f)) * 0.6f;
        float inputRight = (delayed.Right + (delayed.Left * 0.35f)) * 0.6f;
        float coefficient = damping.Next();
        float wetLeft = 0;
        float wetRight = 0;

        for (int index = 0; index < leftCombs.Length; index++)
        {
            wetLeft += leftCombs[index].Process(inputLeft, coefficient) * 0.25f;
            wetRight += rightCombs[index].Process(inputRight, coefficient) * 0.25f;
        }

        for (int index = 0; index < leftDiffusers.Length; index++)
        {
            wetLeft = leftDiffusers[index].Process(wetLeft);
            wetRight = rightDiffusers[index].Process(wetRight);
        }

        if (!input && (--tailFrames <= 0 || IsEmpty()))
        {
            Reset();
        }

        float outputLeft = wetLeft * level;
        float outputRight = wetRight * level;
        return new(outputLeft, outputRight);
    }

    public void Reset()
    {
        preDelay.Reset();
        preDelayTap.Reset();
        damping.Reset();

        for (int index = 0; index < leftCombs.Length; index++)
        {
            leftCombs[index].Reset();
            rightCombs[index].Reset();
        }

        for (int index = 0; index < leftDiffusers.Length; index++)
        {
            leftDiffusers[index].Reset();
            rightDiffusers[index].Reset();
        }

        IsActive = false;
        tailFrames = 0;
    }

    private bool IsEmpty()
    {
        if (!preDelay.IsEmpty)
        {
            return false;
        }

        for (int index = 0; index < leftCombs.Length; index++)
        {
            if (!leftCombs[index].IsEmpty || !rightCombs[index].IsEmpty)
            {
                return false;
            }
        }

        for (int index = 0; index < leftDiffusers.Length; index++)
        {
            if (!leftDiffusers[index].IsEmpty || !rightDiffusers[index].IsEmpty)
            {
                return false;
            }
        }

        return true;
    }

    private static int Scale(int samples, int sampleRate)
    {
        return Math.Max(1, (int)Math.Round(samples * (sampleRate / 44100.0)));
    }
}
