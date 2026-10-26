namespace IgnitionSwitch.Core.Domain;

/// <summary>Identifies a track's clip in a stable scene slot.</summary>
/// <param name="Track">The zero-based track index.</param>
/// <param name="SceneId">The stable scene slot, from zero to seven.</param>
public readonly record struct ClipAddress(int Track, int SceneId);
