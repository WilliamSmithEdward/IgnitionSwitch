using System.Text.Json.Serialization;

namespace IgnitionSwitch.Core.Domain;

/// <summary>A track's persistent mixer state and uniquely owned clip content.</summary>
public sealed record TrackSnapshot
{
    /// <summary>Constructs a track. Legacy notes belong to scene zero; explicit clips replace that input.</summary>
    public TrackSnapshot(
        string Name,
        InstrumentKind Instrument,
        bool IsMuted,
        float Volume,
        IReadOnlyList<LoopNote> Notes,
        TrackSoundSettings? Sound = null,
        IReadOnlyList<ClipSnapshot>? Clips = null
    )
    {
        this.Name = Name;
        this.Instrument = Instrument;
        this.IsMuted = IsMuted;
        this.Volume = Volume;
        this.Sound = Sound;
        this.Clips = Clips ?? [new(0, Notes)];
    }

    /// <summary>Gets the displayed track name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the synthesized instrument.</summary>
    public InstrumentKind Instrument { get; init; }

    /// <summary>Gets the persistent mute target.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Gets the track gain.</summary>
    public float Volume { get; init; }

    /// <summary>Gets the instrument targets.</summary>
    public TrackSoundSettings? Sound { get; init; }

    /// <summary>Gets the uniquely owned clip notes.</summary>
    public IReadOnlyList<ClipSnapshot> Clips { get; init; }

    /// <summary>Gets or replaces the legacy scene-zero projection. It is empty when scene zero is absent.</summary>
    [JsonIgnore]
    public IReadOnlyList<LoopNote> Notes
    {
        get => Clips?.FirstOrDefault(clip => clip.SceneId == 0)?.Notes ?? [];
        init
        {
            List<ClipSnapshot> clips = Clips is null ? [] : [.. Clips];
            int index = clips.FindIndex(clip => clip.SceneId == 0);

            if (index < 0)
            {
                clips.Insert(0, new(0, value));
            }

            else
            {
                clips[index] = new(0, value);
            }

            Clips = clips.AsReadOnly();
        }
    }

    /// <summary>Deconstructs the source-compatible legacy scene-zero view and mixer fields.</summary>
    public void Deconstruct(
        out string name,
        out InstrumentKind instrument,
        out bool isMuted,
        out float volume,
        out IReadOnlyList<LoopNote> notes,
        out TrackSoundSettings? sound
    )
    {
        name = Name;
        instrument = Instrument;
        isMuted = IsMuted;
        volume = Volume;
        notes = Notes;
        sound = Sound;
    }
}
