using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Audio.Mixing;

internal sealed class EffectTargets
{
    private readonly float[] values = new float[8];

    public EffectTargets()
    {
        Apply(ParameterCatalog.DelayDefaults, ParameterCatalog.ReverbDefaults);
    }

    public SmoothedValue DelayReturn { get; } = new(0.25f);
    public SmoothedValue ReverbReturn { get; } = new(0.25f);
    public float DelayTimeSeconds { get; private set; }
    public float DelayFeedback { get; private set; }
    public float DelayDamping { get; private set; }
    public float ReverbDecaySeconds { get; private set; }
    public float ReverbDamping { get; private set; }
    public float ReverbPreDelaySeconds { get; private set; }

    public float Get(ParameterId id)
    {
        return values[(int)id - (int)ParameterId.DelayTime];
    }

    public void Set(ParameterId id, float value, bool immediate)
    {
        values[(int)id - (int)ParameterId.DelayTime] = value;
        float physical = (float)ParameterCatalog
            .Get(id)
            .ToPhysical(value);

        switch (id)
        {
            case ParameterId.DelayTime:
                DelayTimeSeconds = physical;
                break;

            case ParameterId.DelayFeedback:
                DelayFeedback = physical;
                break;

            case ParameterId.DelayDamping:
                DelayDamping = physical;
                break;

            case ParameterId.DelayReturn:
                DelayReturn.SetTarget(value, immediate);
                break;

            case ParameterId.ReverbDecay:
                ReverbDecaySeconds = physical;
                break;

            case ParameterId.ReverbDamping:
                ReverbDamping = physical;
                break;

            case ParameterId.ReverbPreDelay:
                ReverbPreDelaySeconds = physical;
                break;

            case ParameterId.ReverbReturn:
                ReverbReturn.SetTarget(value, immediate);
                break;

            case ParameterId.Tone:
            case ParameterId.Attack:
            case ParameterId.Release:
            case ParameterId.Drive:
            case ParameterId.Pan:
            case ParameterId.SendA:
            case ParameterId.SendB:
            case ParameterId.Level:
            case ParameterId.MasterVolume:
            case ParameterId.Tempo:
            case ParameterId.TrackMute:
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    public DelaySettings DelaySnapshot()
    {
        return new(
            Get(ParameterId.DelayTime),
            Get(ParameterId.DelayFeedback),
            Get(ParameterId.DelayDamping),
            Get(ParameterId.DelayReturn)
        );
    }

    public ReverbSettings ReverbSnapshot()
    {
        return new(
            Get(ParameterId.ReverbDecay),
            Get(ParameterId.ReverbDamping),
            Get(ParameterId.ReverbPreDelay),
            Get(ParameterId.ReverbReturn)
        );
    }

    public void Apply(DelaySettings delay, ReverbSettings reverb)
    {
        float[] targets = [
            delay.Time, delay.Feedback, delay.Damping, delay.Return, reverb.Decay, reverb.Damping,
            reverb.PreDelay, reverb.Return
        ];

        for (int index = 0; index < targets.Length; index++)
        {
            Set(ParameterId.DelayTime + index, targets[index], true);
        }
    }
}
