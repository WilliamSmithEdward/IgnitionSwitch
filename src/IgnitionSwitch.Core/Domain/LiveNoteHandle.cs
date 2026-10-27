namespace IgnitionSwitch.Core.Domain;

/// <summary>An opaque, engine-scoped identity for one live gate. It does not identify sequenced notes.</summary>
public readonly record struct LiveNoteHandle
{
    internal LiveNoteHandle(long engineId, long gateId)
    {
        EngineId = engineId;
        GateId = gateId;
    }

    internal long EngineId { get; }
    internal long GateId { get; }

    /// <summary>Gets whether this is an issued identity. Current liveness is checked by SetLiveExpression; a released or stolen handle can remain structurally valid.</summary>
    public bool IsValid => EngineId != 0 && GateId != 0;
}
