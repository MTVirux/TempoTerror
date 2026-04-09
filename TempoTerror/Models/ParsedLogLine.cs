namespace TempoTerror.Models;

public readonly record struct ParsedLogLine(
    int Type,
    uint SourceId,
    string SourceName,
    uint ActionId,
    string ActionName,
    uint TargetId,
    float CastTime);
