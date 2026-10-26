using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;

namespace IgnitionSwitch.Core.Audio;

internal sealed class SynthVoice(
    int id,
    int track,
    int note,
    int velocity,
    InstrumentKind instrument,
    bool isLoop
)
{
    private const int MinimumOnsetFrames = WorkstationEngine.SampleRate / 250;
    private readonly InstrumentKind _instrument = instrument;
    private readonly double _frequency = 440 * Math.Pow(2, (note - 69) / 12.0);
    private readonly float _gain = (float)Math.Pow(velocity / 127.0, 1.4) * 0.17f;
    private readonly int _note = note;
    private long _age;
    private long _releaseAge = -1;
    private float _releaseLevel;
    private bool _releasePending;
    private uint _noise = 123456789;
    private double _phase;

    public int Id { get; } = id;
    public int Track { get; } = track;
    public bool IsLoop { get; } = isLoop;
    public bool IsFinished { get; private set; }

    public void Release()
    {
        if (_releaseAge >= 0 || _instrument == InstrumentKind.Drums)
        {
            return;
        }

        if (_age < MinimumOnsetFrames)
        {
            _releasePending = true;
            return;
        }

        _releaseLevel = Envelope();
        _releaseAge = _age;
        _releasePending = false;
    }

    public float Render(double pitchRatio)
    {
        if (_releasePending && _age >= MinimumOnsetFrames)
        {
            Release();
        }

        double seconds = (double)_age / WorkstationEngine.SampleRate;
        float envelope = Envelope();
        double wave;

        if (_instrument == InstrumentKind.Drums)
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            double noise = ((_noise & 65535) / 32767.5) - 1;
            wave = _note switch
            {
                36 or 35 => Math.Sin(
                    2 * Math.PI * ((47 * seconds) + (2.2 * (1 - Math.Exp(-seconds * 35))))
                )
                    * Math.Exp(-seconds * 8),
                38 or 40 => ((noise * 0.7) + (Math.Sin(2 * Math.PI * 185 * seconds) * 0.3))
                    * Math.Exp(-seconds * 17),
                42 or 44 => noise * Math.Exp(-seconds * 55) * 0.6,
                46 => noise * Math.Exp(-seconds * 12) * 0.5,
                39 => noise * Math.Exp(-seconds * 22),
                _ => ((Math.Sin(2 * Math.PI * _frequency * seconds) * 0.7) + (noise * 0.3))
                    * Math.Exp(-seconds * 12)
            };

            if (seconds >= 1.5)
            {
                IsFinished = true;
            }
        }

        else
        {
            _phase += _frequency * pitchRatio / WorkstationEngine.SampleRate;
            _phase -= Math.Floor(_phase);
            double sine = Math.Sin(2 * Math.PI * _phase);
            wave = _instrument switch
            {
                InstrumentKind.Keys => sine
                    + (Math.Sin(4 * Math.PI * _phase) * 0.28 * Math.Exp(-seconds * 3))
                    + (Math.Sin(6 * Math.PI * _phase) * 0.12 * Math.Exp(-seconds * 5)),
                InstrumentKind.Bass => (sine * 0.8)
                    + (Math.Sin(4 * Math.PI * _phase) * 0.16)
                    + (Math.Sin(6 * Math.PI * _phase) * 0.08),
                InstrumentKind.Atmosphere => (sine * 0.6)
                    + (Math.Sin((2 * Math.PI * _phase) + (seconds * 0.6)) * 0.3)
                    + (Math.Sin(4 * Math.PI * _phase) * 0.1),
                InstrumentKind.Drums => 0,
                _ => 0
            };
        }

        _age++;
        return (float)wave * envelope * _gain;
    }

    private float Envelope()
    {
        if (_releaseAge >= 0)
        {
            double releaseSeconds = (double)(_age - _releaseAge) / WorkstationEngine.SampleRate;
            double releaseLength = _instrument == InstrumentKind.Atmosphere ? 0.7 : 0.12;

            if (releaseSeconds >= releaseLength)
            {
                IsFinished = true;
                return 0;
            }

            return _releaseLevel * (float)(1 - (releaseSeconds / releaseLength));
        }

        double seconds = (double)_age / WorkstationEngine.SampleRate;
        double attack = _instrument == InstrumentKind.Atmosphere ? 0.08 : 0.004;
        double sustain = _instrument switch
        {
            InstrumentKind.Keys => 0.3 + (0.7 * Math.Exp(-seconds * 1.7)),
            InstrumentKind.Bass => 0.65 + (0.35 * Math.Exp(-seconds * 5)),
            InstrumentKind.Atmosphere or InstrumentKind.Drums => 1,
            _ => 1
        };
        return (float)(Math.Min(1, seconds / attack) * sustain);
    }
}
