namespace TempoTerror.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Plugin.Services;
using TempoTerror.Helpers;
using TempoTerror.Models;

public sealed class ActionTracker : IDisposable
{
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly IinactSubscriber subscriber;
    private readonly ConcurrentDictionary<uint, List<TrackedAction>> actionsByActor = new();
    private readonly ConcurrentDictionary<uint, string> knownActors = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public IReadOnlyDictionary<uint, string> KnownActors => this.knownActors;

    public ActionTracker(
        IObjectTable objectTable,
        IDataManager dataManager,
        IPluginLog log,
        IinactSubscriber subscriber)
    {
        this.objectTable = objectTable;
        this.dataManager = dataManager;
        this.log = log;
        this.subscriber = subscriber;

        this.subscriber.OnLogLine += this.HandleLogLine;
    }

    public double CurrentTime => this.stopwatch.Elapsed.TotalSeconds;

    public uint LocalPlayerId => this.objectTable.LocalPlayer?.EntityId ?? 0;

    public List<TrackedAction> GetActions(uint actorId)
    {
        if (!this.actionsByActor.TryGetValue(actorId, out var list))
            return [];

        lock (list)
        {
            return [.. list];
        }
    }

    public void Prune(float displayTimeSeconds)
    {
        var cutoff = this.CurrentTime - displayTimeSeconds - 2.0;

        foreach (var kvp in this.actionsByActor)
        {
            lock (kvp.Value)
            {
                kvp.Value.RemoveAll(a => a.Timestamp < cutoff);
            }
        }
    }

    public void Dispose()
    {
        this.subscriber.OnLogLine -= this.HandleLogLine;
    }

    private void HandleLogLine(string line)
    {
        if (!LogLineParser.TryParse(line, out var parsed))
            return;

        switch (parsed.Type)
        {
            case 20:
                this.HandleStartsCasting(parsed);
                break;
            case 21:
            case 22:
                this.HandleAbility(parsed);
                break;
            case 23:
                this.HandleCancelAbility(parsed);
                break;
        }
    }

    private void HandleStartsCasting(ParsedLogLine parsed)
    {
        var action = this.CreateTrackedAction(parsed);
        if (action is null)
            return;

        action.CastTime = parsed.CastTime;
        action.IsCasting = true;
        this.AddAction(action);
    }

    private void HandleAbility(ParsedLogLine parsed)
    {
        var list = this.GetOrCreateList(parsed.SourceId);

        lock (list)
        {
            // Resolve an active cast for this action.
            var casting = list.FindLast(a =>
                a.ActionId == parsed.ActionId &&
                a.SourceId == parsed.SourceId &&
                a.IsCasting);

            if (casting is not null)
            {
                casting.IsCasting = false;
                casting.ResolvedAt = DateTime.UtcNow;
                return;
            }
        }

        // Instant ability — no prior cast.
        var action = this.CreateTrackedAction(parsed);
        if (action is not null)
            this.AddAction(action);
    }

    private void HandleCancelAbility(ParsedLogLine parsed)
    {
        if (!this.actionsByActor.TryGetValue(parsed.SourceId, out var list))
            return;

        lock (list)
        {
            var casting = list.FindLast(a =>
                a.ActionId == parsed.ActionId &&
                a.SourceId == parsed.SourceId &&
                a.IsCasting);

            if (casting is not null)
            {
                casting.IsCasting = false;
                casting.WasCancelled = true;
                casting.ResolvedAt = DateTime.UtcNow;
            }
        }
    }

    private TrackedAction? CreateTrackedAction(ParsedLogLine parsed)
    {
        var actionRow = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(parsed.ActionId);
        if (actionRow is null)
            return null;

        var actionType = ClassifyAction(actionRow.Value, parsed.SourceId);
        var iconId = (ushort)actionRow.Value.Icon;

        this.knownActors.TryAdd(parsed.SourceId, parsed.SourceName);

        return new TrackedAction
        {
            ActionId = parsed.ActionId,
            ActionName = parsed.ActionName,
            SourceId = parsed.SourceId,
            SourceName = parsed.SourceName,
            Timestamp = this.CurrentTime,
            ActionType = actionType,
            IconId = iconId,
        };
    }

    private ActionType ClassifyAction(Lumina.Excel.Sheets.Action action, uint sourceId)
    {
        // CooldownGroup 58 = GCD
        if (action.CooldownGroup == 58)
            return ActionType.Gcd;

        // CooldownGroup 0 + ActionCategory 1 = Auto-attack
        if (action.CooldownGroup == 0 && action.ActionCategory.RowId == 1)
            return ActionType.AutoAttack;

        // Pet detection: source is not the local player and not a known party member.
        // This is a heuristic; pets have owner IDs that differ from the party list.
        if (sourceId != this.LocalPlayerId && action.ActionCategory.RowId == 11)
            return ActionType.Pet;

        return ActionType.Ogcd;
    }

    private void AddAction(TrackedAction action)
    {
        var list = this.GetOrCreateList(action.SourceId);

        lock (list)
        {
            if (list.Count >= ConfigStatic.MaxActionsPerActor)
                list.RemoveAt(0);

            list.Add(action);
        }
    }

    private List<TrackedAction> GetOrCreateList(uint actorId)
    {
        return this.actionsByActor.GetOrAdd(actorId, _ => new List<TrackedAction>());
    }
}
