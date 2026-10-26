using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Audio.Mixing;

internal sealed class TrackSoundState
{
    private readonly InstrumentKind instrument;
    private float lastPan = 0.5f;

    public TrackSoundState(InstrumentKind instrument)
    {
        this.instrument = instrument;
        TrackSoundSettings defaults = ParameterCatalog.TrackDefaults(instrument);
        Attack = defaults.Attack;
        Release = defaults.Release;
        RefreshEnvelope();
    }

    public SmoothedValue Tone { get; } = new(0.5f);
    public SmoothedValue Drive { get; } = new(0);
    public SmoothedValue Pan { get; } = new(0.5f);
    public SmoothedValue SendA { get; } = new(0);
    public SmoothedValue SendB { get; } = new(0);
    public SmoothedValue Level { get; } = new(0.8f);
    public float Attack { get; private set; }
    public float Release { get; private set; }
    public double AttackSeconds { get; private set; }
    public double ReleasePhysical { get; private set; }
    public float LeftGain { get; private set; } = 1;
    public float RightGain { get; private set; } = 1;

    public TrackSoundSettings Snapshot()
    {
        return new(
            Tone.Target,
            Attack,
            Release,
            Drive.Target,
            Pan.Target,
            SendA.Target,
            SendB.Target
        );
    }

    public float Get(ParameterId id)
    {
        return id switch
        {
            ParameterId.Tone => Tone.Target,
            ParameterId.Attack => Attack,
            ParameterId.Release => Release,
            ParameterId.Drive => Drive.Target,
            ParameterId.Pan => Pan.Target,
            ParameterId.SendA => SendA.Target,
            ParameterId.SendB => SendB.Target,
            ParameterId.Level => Level.Target,
            ParameterId.MasterVolume => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.Tempo => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.TrackMute => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.DelayTime => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.DelayFeedback => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.DelayDamping => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.DelayReturn => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.ReverbDecay => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.ReverbDamping => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.ReverbPreDelay => throw new ArgumentOutOfRangeException(nameof(id)),
            ParameterId.ReverbReturn => throw new ArgumentOutOfRangeException(nameof(id)),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }

    public void Set(ParameterId id, float value, bool immediate)
    {
        switch (id)
        {
            case ParameterId.Tone:
                Tone.SetTarget(value, immediate);
                break;

            case ParameterId.Attack:
                Attack = value;
                RefreshEnvelope();
                break;

            case ParameterId.Release:
                Release = value;
                RefreshEnvelope();
                break;

            case ParameterId.Drive:
                Drive.SetTarget(value, immediate);
                break;

            case ParameterId.Pan:
                Pan.SetTarget(value, immediate);
                break;

            case ParameterId.SendA:
                SendA.SetTarget(value, immediate);
                break;

            case ParameterId.SendB:
                SendB.SetTarget(value, immediate);
                break;

            case ParameterId.Level:
                Level.SetTarget(value, immediate);
                break;

            case ParameterId.MasterVolume:
            case ParameterId.Tempo:
            case ParameterId.TrackMute:
            case ParameterId.DelayTime:
            case ParameterId.DelayFeedback:
            case ParameterId.DelayDamping:
            case ParameterId.DelayReturn:
            case ParameterId.ReverbDecay:
            case ParameterId.ReverbDamping:
            case ParameterId.ReverbPreDelay:
            case ParameterId.ReverbReturn:
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    public void Apply(TrackSoundSettings settings, float volume)
    {
        Tone.SetTarget(settings.Tone, true);
        Attack = settings.Attack;
        Release = settings.Release;
        Drive.SetTarget(settings.Drive, true);
        Pan.SetTarget(settings.Pan, true);
        SendA.SetTarget(settings.SendA, true);
        SendB.SetTarget(settings.SendB, true);
        Level.SetTarget(volume, true);
        RefreshEnvelope();
        Advance();
    }

    public void Advance()
    {
        _ = Tone.Next();
        _ = Drive.Next();
        _ = SendA.Next();
        _ = SendB.Next();
        _ = Level.Next();
        float pan = Pan.Next();

        if (pan != lastPan)
        {
            lastPan = pan;

            if (pan == 0.5f)
            {
                LeftGain = 1;
                RightGain = 1;
            }

            else
            {
                float angle = pan * MathF.PI / 2;
                LeftGain = pan == 1 ? 0 : MathF.Sqrt(2) * MathF.Cos(angle);
                RightGain = pan == 0 ? 0 : MathF.Sqrt(2) * MathF.Sin(angle);
            }
        }
    }

    private void RefreshEnvelope()
    {
        AttackSeconds = ParameterCatalog
            .Get(ParameterId.Attack, instrument)
            .ToPhysical(Attack);
        ReleasePhysical = ParameterCatalog
            .Get(ParameterId.Release, instrument)
            .ToPhysical(Release);
    }
}
