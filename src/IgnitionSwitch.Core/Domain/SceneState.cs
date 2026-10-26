namespace IgnitionSwitch.Core.Domain;

/// <summary>Persistent scene metadata in display order.</summary>
/// <param name="Id">The stable scene slot.</param>
/// <param name="Name">The displayed scene name.</param>
public sealed record SceneState(int Id, string Name);
