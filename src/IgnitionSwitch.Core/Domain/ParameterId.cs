namespace IgnitionSwitch.Core.Domain;

/// <summary>Hardware-independent sound and mixer parameter identities.</summary>
public enum ParameterId
{
    /// <summary>Instrument tonal balance.</summary>
    Tone,
    /// <summary>Instrument attack time.</summary>
    Attack,
    /// <summary>Melodic release time or drum tail scale.</summary>
    Release,
    /// <summary>Instrument saturation amount.</summary>
    Drive,
    /// <summary>Track stereo position.</summary>
    Pan,
    /// <summary>Post-fader delay send.</summary>
    SendA,
    /// <summary>Post-fader reverb send.</summary>
    SendB,
    /// <summary>The existing track volume target.</summary>
    Level,
    /// <summary>Master output gain.</summary>
    MasterVolume,
    /// <summary>Project tempo.</summary>
    Tempo,
    /// <summary>Track mute, zero or one.</summary>
    TrackMute,
    /// <summary>Delay time.</summary>
    DelayTime,
    /// <summary>Delay feedback.</summary>
    DelayFeedback,
    /// <summary>Delay high-frequency damping.</summary>
    DelayDamping,
    /// <summary>Delay return gain.</summary>
    DelayReturn,
    /// <summary>Reverb decay time.</summary>
    ReverbDecay,
    /// <summary>Reverb high-frequency damping.</summary>
    ReverbDamping,
    /// <summary>Reverb pre-delay.</summary>
    ReverbPreDelay,
    /// <summary>Reverb return gain.</summary>
    ReverbReturn
}
