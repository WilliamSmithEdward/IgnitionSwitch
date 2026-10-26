using IgnitionSwitch.Core.Audio.Mixing;
using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly EffectTargets _effects = new();
    private readonly SmoothedValue _masterGain = new(0.65f);

    /// <summary>Gets the validated parameter's normalized target, independently of smoothing progress.</summary>
    public double GetParameter(ParameterAddress address)
    {
        ValidateAddress(address);

        lock (_sync)
        {
            ThrowIfDisposed();
            double stored = GetStoredParameter(address);
            return address.Id == ParameterId.Tempo
                ? ParameterCatalog
                    .Get(address.Id)
                    .ToNormalized(stored)
                : stored;
        }
    }

    /// <summary>Sets a finite normalized target from zero through one through the shared bounded history path.</summary>
    public void SetParameter(ParameterAddress address, double normalized)
    {
        ValidateAddress(address);
        ValidateRange(normalized, 0, 1, nameof(normalized));

        lock (_sync)
        {
            ThrowIfDisposed();

            if (address.Id == ParameterId.Tempo)
            {
                if (GetParameter(address) != normalized)
                {
                    SetStoredParameter(
                        address,
                        ParameterCatalog
                            .Get(address.Id)
                            .ToPhysical(normalized)
                    );
                }
            }

            else
            {
                SetStoredParameter(address, normalized);
            }
        }
    }

    /// <summary>Gets cached physical ranges, defaults, conversion, and formatting for a parameter target.</summary>
    public ParameterDescriptor GetDescriptor(ParameterAddress address)
    {
        ValidateAddress(address);

        lock (_sync)
        {
            ThrowIfDisposed();
            InstrumentKind instrument = address.Track < 0
                ? InstrumentKind.Keys
                : _tracks[address.Track].Instrument;
            return ParameterCatalog.Get(address.Id, instrument);
        }
    }

    private static void ValidateAddress(ParameterAddress address)
    {
        if (!Enum.IsDefined(address.Id))
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }

        if (ParameterCatalog.IsTrack(address.Id))
        {
            ValidateTrack(address.Track);
        }

        else if (address.Track != -1)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }
    }

    private double GetStoredParameter(ParameterAddress address)
    {
        return address.Id switch
        {
            ParameterId.Tempo => _tempo,
            ParameterId.MasterVolume => _masterVolume,
            ParameterId.TrackMute => _tracks[address.Track].IsMuted ? 1 : 0,
            ParameterId.DelayTime
                or ParameterId.DelayFeedback
                or ParameterId.DelayDamping
                or ParameterId.DelayReturn
                or ParameterId.ReverbDecay
                or ParameterId.ReverbDamping
                or ParameterId.ReverbPreDelay
                or ParameterId.ReverbReturn => _effects.Get(address.Id),
            ParameterId.Tone
                or ParameterId.Attack
                or ParameterId.Release
                or ParameterId.Drive
                or ParameterId.Pan
                or ParameterId.SendA
                or ParameterId.SendB
                or ParameterId.Level => _tracks[address.Track].Sound.Get(address.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(address))
        };
    }

    private void SetStoredParameter(ParameterAddress address, double value)
    {
        double target = address.Id == ParameterId.Tempo
            ? value
            : address.Id == ParameterId.TrackMute ? value >= 0.5 ? 1 : 0 : (float)value;
        double before = GetStoredParameter(address);

        if (before == target)
        {
            return;
        }

        _history.RecordScalar(new(address, before, target), address.Id != ParameterId.TrackMute);
        ApplyParameterValue(address, target);
    }

    private void ApplyParameterValue(ParameterAddress address, double value)
    {
        bool immediate = !_isPlaying
            && _voices.Count == 0
            && !_liveEffects.IsActive
            && !_sequencedEffects.IsActive;

        switch (address.Id)
        {
            case ParameterId.Tempo:
                _tempo = value;
                break;

            case ParameterId.MasterVolume:
                _masterVolume = (float)value;
                _masterGain.SetTarget(_masterVolume, immediate);
                break;

            case ParameterId.TrackMute:
                _tracks[address.Track].IsMuted = value != 0;
                break;

            case ParameterId.DelayTime:
            case ParameterId.DelayFeedback:
            case ParameterId.DelayDamping:
            case ParameterId.DelayReturn:
            case ParameterId.ReverbDecay:
            case ParameterId.ReverbDamping:
            case ParameterId.ReverbPreDelay:
            case ParameterId.ReverbReturn:
                _effects.Set(address.Id, (float)value, immediate);
                break;

            case ParameterId.Tone:
            case ParameterId.Attack:
            case ParameterId.Release:
            case ParameterId.Drive:
            case ParameterId.Pan:
            case ParameterId.SendA:
            case ParameterId.SendB:
            case ParameterId.Level:
                _tracks[address.Track].Sound.Set(address.Id, (float)value, immediate);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(address));
        }
    }
}
