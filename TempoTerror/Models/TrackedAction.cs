namespace TempoTerror.Models;

using System;

public sealed class TrackedAction
{
    public required uint ActionId { get; init; }

    public required string ActionName { get; init; }

    public required uint SourceId { get; init; }

    public required string SourceName { get; init; }

    public required double Timestamp { get; init; }

    public required ActionType ActionType { get; init; }

    public required ushort IconId { get; init; }

    public float CastTime { get; set; }

    public bool IsCasting { get; set; }

    public bool WasCancelled { get; set; }

    public DateTime ResolvedAt { get; set; }
}
