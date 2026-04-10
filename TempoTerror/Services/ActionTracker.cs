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
    private readonly IPluginLog log;
    private readonly IDataSource subscriber;
    private readonly ConcurrentDictionary<uint, List<TrackedAction>> actionsByActor = new();
    private readonly ConcurrentDictionary<uint, string> knownActors = new();
    private readonly ConcurrentQueue<string> pendingLines = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Action> actionSheet;
    private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> itemSheet;
    private readonly Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Mount> mountSheet;

    public IReadOnlyDictionary<uint, string> KnownActors => this.knownActors;

    public ActionTracker(
        IObjectTable objectTable,
        IDataManager dataManager,
        IPluginLog log,
        IDataSource subscriber)
    {
        this.objectTable = objectTable;
        this.log = log;
        this.subscriber = subscriber;
        this.actionSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        this.itemSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        this.mountSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();

        this.subscriber.OnLogLine += this.EnqueueLogLine;
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
        this.subscriber.OnLogLine -= this.EnqueueLogLine;
    }

    private void EnqueueLogLine(string line)
    {
        this.pendingLines.Enqueue(line);
    }

    public void ProcessPendingLines()
    {
        var budget = 50;
        while (budget-- > 0 && this.pendingLines.TryDequeue(out var line))
        {
            this.HandleLogLine(line);
        }
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
                this.log.Information("[HandleLogLine] Type {Type}: {Source} ({SourceId:X}) -> {Action} (id={ActionId})",
                    parsed.Type, parsed.SourceName, parsed.SourceId, parsed.ActionName, parsed.ActionId);
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
        this.log.Verbose("[StartsCasting] {Source} ({SourceId:X}) -> {Action} ({ActionId}) cast={CastTime:F2}s type={Type}",
            parsed.SourceName, parsed.SourceId, parsed.ActionName, parsed.ActionId, parsed.CastTime, action.ActionType);
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
                this.log.Information("[AbilityResolved] {Source} ({SourceId:X}) -> {Action} (id={ActionId}) type={Type}",
                    parsed.SourceName, parsed.SourceId, parsed.ActionName, parsed.ActionId, casting.ActionType);
                return;
            }
        }

        // Instant ability — no prior cast.
        var action = this.CreateTrackedAction(parsed);
        if (action is null)
            return;

        this.AddAction(action);
        this.log.Information("[Ability] {Source} ({SourceId:X}) -> {Action} (id={ActionId}) type={Type}",
            parsed.SourceName, parsed.SourceId, parsed.ActionName, parsed.ActionId, action.ActionType);
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
                this.log.Verbose("[CancelAbility] {Source} ({SourceId:X}) -> {Action} ({ActionId}) type={Type}",
                    parsed.SourceName, parsed.SourceId, parsed.ActionName, parsed.ActionId, casting.ActionType);
            }
        }
    }

    private TrackedAction? CreateTrackedAction(ParsedLogLine parsed)
    {
        // Mount usage: action IDs in the 0xD000000 range are encoded mount IDs.
        if (parsed.ActionId >= 0xD000000 && parsed.ActionId < 0xE000000)
            return this.CreateMountAction(parsed);

        // Item usage: action IDs with the 0x2000000 bit are encoded item IDs.
        if (parsed.ActionId >= 0x2000000)
            return this.CreateItemAction(parsed);

        var actionRow = this.actionSheet.GetRowOrDefault(parsed.ActionId);

        ActionType actionType;
        ushort iconId;

        if (actionRow is not null)
        {
            var classified = ClassifyAction(actionRow.Value, parsed.SourceId);
            if (classified is null)
                return null;

            actionType = classified.Value;
            iconId = actionType == ActionType.AutoAttack ? (ushort)101
                : parsed.ActionId == 4 ? (ushort)58
                : (ushort)actionRow.Value.Icon;
        }
        else
        {
            this.log.Warning("[CreateTrackedAction] Lumina lookup failed for actionId={ActionId} ({ActionName}) from {Source} ({SourceId:X})",
                parsed.ActionId, parsed.ActionName, parsed.SourceName, parsed.SourceId);
            return null;
        }

        // Don't add pet entities to the known actors list.
        if (actionType != ActionType.Pet)
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

    private TrackedAction? CreateMountAction(ParsedLogLine parsed)
    {
        var mountId = parsed.ActionId - 0xD000000;
        var mountRow = this.mountSheet.GetRowOrDefault(mountId);
        var iconId = mountRow is not null ? (ushort)mountRow.Value.Icon : (ushort)0;
        var mountName = mountRow is not null ? mountRow.Value.Singular.ToString() : parsed.ActionName;

        this.log.Information("[MountAction] {Source} ({SourceId:X}) used mount {Name} (mountId={MountId} icon={Icon})",
            parsed.SourceName, parsed.SourceId, mountName, mountId, iconId);

        this.knownActors.TryAdd(parsed.SourceId, parsed.SourceName);

        return new TrackedAction
        {
            ActionId = parsed.ActionId,
            ActionName = mountName,
            SourceId = parsed.SourceId,
            SourceName = parsed.SourceName,
            Timestamp = this.CurrentTime,
            ActionType = ActionType.Ogcd,
            IconId = iconId,
        };
    }

    private TrackedAction? CreateItemAction(ParsedLogLine parsed)
    {
        var itemId = parsed.ActionId - 0x2000000;

        // HQ items have 1,000,000 added to their ID.
        if (itemId >= 1_000_000)
            itemId -= 1_000_000;

        var itemRow = this.itemSheet.GetRowOrDefault(itemId);
        var iconId = itemRow is not null ? (ushort)itemRow.Value.Icon : (ushort)0;
        var itemName = itemRow is not null ? itemRow.Value.Name.ToString() : parsed.ActionName;

        this.log.Information("[ItemAction] {Source} ({SourceId:X}) used item {Name} (itemId={ItemId} icon={Icon})",
            parsed.SourceName, parsed.SourceId, itemName, itemId, iconId);

        this.knownActors.TryAdd(parsed.SourceId, parsed.SourceName);

        return new TrackedAction
        {
            ActionId = parsed.ActionId,
            ActionName = itemName,
            SourceId = parsed.SourceId,
            SourceName = parsed.SourceName,
            Timestamp = this.CurrentTime,
            ActionType = ActionType.Ogcd,
            IconId = iconId,
        };
    }

    private ActionType? ClassifyAction(Lumina.Excel.Sheets.Action action, uint sourceId)
    {
        var cooldownGroup = action.CooldownGroup;
        var category = action.ActionCategory.RowId;

        this.log.Information("[Classify] {Name} (id={RowId}) CooldownGroup={CdGroup} ActionCategory={Category} source={SourceId:X}",
            action.Name.ToString(), action.RowId, cooldownGroup, category, sourceId);

        // Non-player entities in the 0x40000000 range are BattleNpcs (pets AND enemies).
        // Only classify as pet if the entity actually has an owner; otherwise it's an enemy.
        if (sourceId != this.LocalPlayerId && (sourceId & 0x40000000) != 0)
        {
            var ownerId = this.GetPetOwnerId(sourceId);
            if (ownerId != 0)
                return ActionType.Pet;

            // No owner = enemy NPC — skip entirely.
            return null;
        }

        // CooldownGroup 58 = GCD, or Weaponskill (3) / Spell (2) always trigger GCD
        if (cooldownGroup == 58 || category is 2 or 3)
            return ActionType.Gcd;

        // Auto-attack: well-known auto-attack action IDs.
        if (action.RowId is 7 or 8)
            return ActionType.AutoAttack;

        if (cooldownGroup == 0 && category == 1)
            return ActionType.AutoAttack;

        // Everything else (Ability = category 4, etc.) = oGCD
        return ActionType.Ogcd;
    }

    private void AddAction(TrackedAction action)
    {
        // Store pet actions under the pet's owner so they appear on the correct player's timeline.
        var storeId = action.SourceId;
        if (action.ActionType == ActionType.Pet)
        {
            var ownerId = this.GetPetOwnerId(action.SourceId);
            storeId = ownerId != 0 ? ownerId : action.SourceId;
        }

        var list = this.GetOrCreateList(storeId);

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

    private uint GetPetOwnerId(uint petEntityId)
    {
        foreach (var obj in this.objectTable)
        {
            if (obj.EntityId == petEntityId)
                return obj.OwnerId;
        }

        return 0;
    }
}
