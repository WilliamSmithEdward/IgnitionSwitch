using IgnitionSwitch.Core.Audio;
using IgnitionSwitch.Core.Audio.Effects;
using IgnitionSwitch.Core.Audio.Mixing;

namespace IgnitionSwitch.Core.Sequencing;

public sealed partial class WorkstationEngine
{
    private readonly EffectsLane _liveEffects = new(SampleRate);
    private readonly EffectsLane _sequencedEffects = new(SampleRate);
    private readonly float[] _liveMono = new float[TrackCount];
    private readonly float[] _sequencedMono = new float[TrackCount];

    private SynthVoice CreateVoice(int id, int track, int note, int velocity, bool loop)
    {
        Track target = _tracks[track];
        return new(
            id,
            track,
            note,
            velocity,
            target.Instrument,
            loop,
            target.Sound.AttackSeconds,
            target.Sound.ReleasePhysical
        );
    }

    private StereoSample RenderMixedFrame()
    {
        for (int track = 0; track < TrackCount; track++)
        {
            _tracks[track].Sound.Advance();
            _liveMono[track] = 0;
            _sequencedMono[track] = 0;
        }

        bool anySolo = false;

        foreach (Track lane in _tracks)
        {
            anySolo |= lane.IsSolo;
        }

        for (int voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
        {
            SynthVoice voice = _voices[voiceIndex];
            Track track = _tracks[voice.Track];
            TrackSoundState sound = track.Sound;
            float value = voice.Render(
                _pitchRatio,
                sound.Tone.Current,
                sound.Drive.Current,
                sound.ReleasePhysical
            );

            if (!track.IsMuted && (!anySolo || track.IsSolo))
            {
                if (voice.IsLoop)
                {
                    _sequencedMono[voice.Track] += value;
                }

                else
                {
                    _liveMono[voice.Track] += value;
                }
            }

            if (voice.IsFinished)
            {
                _voices.RemoveAt(voiceIndex);
            }
        }

        float dryLeft = 0;
        float dryRight = 0;
        float liveDelayLeft = 0;
        float liveDelayRight = 0;
        float liveRoomLeft = 0;
        float liveRoomRight = 0;
        float loopDelayLeft = 0;
        float loopDelayRight = 0;
        float loopRoomLeft = 0;
        float loopRoomRight = 0;

        for (int track = 0; track < TrackCount; track++)
        {
            TrackSoundState sound = _tracks[track].Sound;
            float live = _liveMono[track] * sound.Level.Current;
            float loop = _sequencedMono[track] * sound.Level.Current;
            float liveLeft = live * sound.LeftGain;
            float liveRight = live * sound.RightGain;
            float loopLeft = loop * sound.LeftGain;
            float loopRight = loop * sound.RightGain;
            dryLeft += liveLeft + loopLeft;
            dryRight += liveRight + loopRight;
            liveDelayLeft += liveLeft * sound.SendA.Current;
            liveDelayRight += liveRight * sound.SendA.Current;
            liveRoomLeft += liveLeft * sound.SendB.Current;
            liveRoomRight += liveRight * sound.SendB.Current;
            loopDelayLeft += loopLeft * sound.SendA.Current;
            loopDelayRight += loopRight * sound.SendA.Current;
            loopRoomLeft += loopLeft * sound.SendB.Current;
            loopRoomRight += loopRight * sound.SendB.Current;
        }

        DelayParameters delay = new(
            _effects.DelayTimeSeconds,
            _effects.DelayFeedback,
            _effects.DelayDamping,
            _effects.DelayReturn.Next()
        );
        ReverbParameters reverb = new(
            _effects.ReverbDecaySeconds,
            _effects.ReverbDamping,
            _effects.ReverbPreDelaySeconds,
            _effects.ReverbReturn.Next()
        );
        StereoSample liveWet = _liveEffects.Process(
            liveDelayLeft,
            liveDelayRight,
            liveRoomLeft,
            liveRoomRight,
            in delay,
            in reverb
        );
        StereoSample loopWet = _sequencedEffects.Process(
            loopDelayLeft,
            loopDelayRight,
            loopRoomLeft,
            loopRoomRight,
            in delay,
            in reverb
        );
        return new(dryLeft + liveWet.Left + loopWet.Left, dryRight + liveWet.Right + loopWet.Right);
    }

    private void ResetEffects()
    {
        _liveEffects.Reset();
        _sequencedEffects.Reset();
    }
}
