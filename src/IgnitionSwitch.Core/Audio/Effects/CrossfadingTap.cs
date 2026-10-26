namespace IgnitionSwitch.Core.Audio.Effects;

// Retargets during a fade coalesce until its endpoint, preserving the current two-tap mix.
internal sealed class CrossfadingTap(int fadeFrames, float initialLag)
{
    private readonly int frames = fadeFrames;
    private float from = initialLag;
    private float to = initialLag;
    private float requested = initialLag;
    private int position = fadeFrames;

    public void SetTarget(float lag, bool active)
    {
        requested = lag;

        if (!active)
        {
            Reset();
        }

        else if (position >= frames && to != requested)
        {
            BeginFade();
        }
    }

    public StereoSample Read(StereoDelayLine line, float extraLag = 0)
    {
        StereoSample target = line.Read(to + extraLag);

        if (position >= frames)
        {
            return target;
        }

        StereoSample source = line.Read(from + extraLag);
        float blend = position / (float)frames;
        StereoSample output = new(
            source.Left + ((target.Left - source.Left) * blend),
            source.Right + ((target.Right - source.Right) * blend)
        );
        position++;

        if (position == frames && to != requested)
        {
            BeginFade();
        }

        return output;
    }

    public void Reset()
    {
        from = requested;
        to = requested;
        position = frames;
    }

    private void BeginFade()
    {
        from = to;
        to = requested;
        position = 0;
    }
}
