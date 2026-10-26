namespace IgnitionSwitch.Core.Domain;

/// <summary>Persistent normalized instrument and routing targets; Level remains TrackSnapshot.Volume.</summary>
/// <param name="Tone">Tonal balance from zero through one, neutral at one half.</param>
/// <param name="Attack">Normalized attack time.</param>
/// <param name="Release">Normalized melodic release or drum tail scale.</param>
/// <param name="Drive">Saturation amount.</param>
/// <param name="Pan">Stereo position, centered at one half.</param>
/// <param name="SendA">Delay send gain.</param>
/// <param name="SendB">Reverb send gain.</param>
public sealed record TrackSoundSettings(
    float Tone,
    float Attack,
    float Release,
    float Drive,
    float Pan,
    float SendA,
    float SendB
);
