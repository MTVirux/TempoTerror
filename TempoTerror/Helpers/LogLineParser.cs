namespace TempoTerror.Helpers;

using System;
using System.Globalization;
using TempoTerror.Models;

public static class LogLineParser
{
    public static bool TryParse(string line, out ParsedLogLine result)
    {
        result = default;

        if (string.IsNullOrEmpty(line))
            return false;

        var span = line.AsSpan();

        // Format: type|timestamp|field2|...  — pipe-delimited
        // First field is the message type.
        var pipeIdx = span.IndexOf('|');
        if (pipeIdx < 0)
            return false;

        if (!int.TryParse(span[..pipeIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
            return false;

        return type switch
        {
            20 => TryParseStartsCasting(span, type, out result),
            21 or 22 => TryParseAbility(span, type, out result),
            23 => TryParseCancelAbility(span, type, out result),
            _ => false,
        };
    }

    // Type 20 — NetworkStartsCasting
    // 20|timestamp|sourceId|sourceName|actionId|actionName|targetId|targetName|castTime|...
    private static bool TryParseStartsCasting(ReadOnlySpan<char> line, int type, out ParsedLogLine result)
    {
        result = default;
        Span<Range> ranges = stackalloc Range[10];
        var count = line.Split(ranges, '|');
        if (count < 9)
            return false;

        if (!TryParseHex(line[ranges[2]], out var sourceId))
            return false;

        var sourceName = line[ranges[3]].ToString();

        if (!TryParseHex(line[ranges[4]], out var actionId))
            return false;

        var actionName = line[ranges[5]].ToString();

        if (!TryParseHex(line[ranges[6]], out var targetId))
            return false;

        if (!float.TryParse(line[ranges[8]], NumberStyles.Float, CultureInfo.InvariantCulture, out var castTime))
            castTime = 0f;

        result = new ParsedLogLine(type, sourceId, sourceName, actionId, actionName, targetId, castTime);
        return true;
    }

    // Type 21/22 — NetworkAbility / NetworkAOEAbility
    // 21|timestamp|sourceId|sourceName|actionId|actionName|targetId|targetName|...
    private static bool TryParseAbility(ReadOnlySpan<char> line, int type, out ParsedLogLine result)
    {
        result = default;
        Span<Range> ranges = stackalloc Range[9];
        var count = line.Split(ranges, '|');
        if (count < 8)
            return false;

        if (!TryParseHex(line[ranges[2]], out var sourceId))
            return false;

        var sourceName = line[ranges[3]].ToString();

        if (!TryParseHex(line[ranges[4]], out var actionId))
            return false;

        var actionName = line[ranges[5]].ToString();

        if (!TryParseHex(line[ranges[6]], out var targetId))
            return false;

        result = new ParsedLogLine(type, sourceId, sourceName, actionId, actionName, targetId, 0f);
        return true;
    }

    // Type 23 — NetworkCancelAbility
    // 23|timestamp|sourceId|sourceName|actionId|actionName|...
    private static bool TryParseCancelAbility(ReadOnlySpan<char> line, int type, out ParsedLogLine result)
    {
        result = default;
        Span<Range> ranges = stackalloc Range[7];
        var count = line.Split(ranges, '|');
        if (count < 6)
            return false;

        if (!TryParseHex(line[ranges[2]], out var sourceId))
            return false;

        var sourceName = line[ranges[3]].ToString();

        if (!TryParseHex(line[ranges[4]], out var actionId))
            return false;

        var actionName = line[ranges[5]].ToString();

        result = new ParsedLogLine(type, sourceId, sourceName, actionId, actionName, 0, 0f);
        return true;
    }

    private static bool TryParseHex(ReadOnlySpan<char> span, out uint value)
    {
        return uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
