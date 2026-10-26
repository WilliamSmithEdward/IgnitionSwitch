namespace IgnitionSwitch.Core.Domain;

/// <summary>A versioned four-track project independent of audio and controller adapters.</summary>
/// <param name="Version">The project schema version, currently 2.</param>
/// <param name="Tempo">Tempo in beats per minute, from 60 to 180.</param>
/// <param name="LoopBars">The loop length, currently four bars.</param>
/// <param name="MasterVolume">The master gain from zero to one.</param>
/// <param name="Tracks">The four track snapshots in display order.</param>
/// <param name="Delay">Version two delay targets.</param>
/// <param name="Reverb">Version two reverb targets.</param>
public sealed record SessionSnapshot(
    int Version,
    double Tempo,
    int LoopBars,
    float MasterVolume,
    IReadOnlyList<TrackSnapshot> Tracks,
    DelaySettings? Delay = null,
    ReverbSettings? Reverb = null
);
