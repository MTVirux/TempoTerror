namespace TempoTerror.Gui.ConfigWindow;

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using TempoTerror.Models;
using TempoTerror.Services;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ActionTracker tracker;

    public ConfigWindow(Configuration config, IDalamudPluginInterface pluginInterface, ActionTracker tracker)
        : base("TempoTerror Settings##ConfigWindow")
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.tracker = tracker;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350, 300),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

        if (ImGui.CollapsingHeader("Player", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var localId = this.tracker.LocalPlayerId;
            var selectedId = this.config.SelectedPlayerId ?? localId;
            var actors = this.tracker.KnownActors;
            var currentLabel = actors.TryGetValue(selectedId, out var name) ? name : "Local Player";

            if (ImGui.BeginCombo("Player", currentLabel))
            {
                var localName = actors.TryGetValue(localId, out var ln) ? ln : "Local Player";
                if (ImGui.Selectable(localName, selectedId == localId))
                {
                    this.config.SelectedPlayerId = null;
                    changed = true;
                }

                foreach (var kvp in actors)
                {
                    if (kvp.Key == localId)
                        continue;

                    if (ImGui.Selectable(kvp.Value, selectedId == kvp.Key))
                    {
                        this.config.SelectedPlayerId = kvp.Key;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }
        }

        if (ImGui.CollapsingHeader("Data Source", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var mode = (int)this.config.DataSourceMode;
            if (ImGui.Combo("Connection Mode", ref mode, "IPC (in-process)\0WebSocket\0"))
            {
                this.config.DataSourceMode = (Models.DataSourceMode)mode;
                changed = true;
            }

            if (this.config.DataSourceMode == Models.DataSourceMode.WebSocket)
            {
                var url = this.config.WebSocketUrl;
                if (ImGui.InputText("WebSocket URL", ref url, 256))
                {
                    this.config.WebSocketUrl = url;
                    changed = true;
                }
            }

            ImGui.TextColored(
                new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                "Changes take effect after plugin reload.");
        }

        if (ImGui.CollapsingHeader("Timeline", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var displayTime = this.config.DisplayTimeSeconds;
            if (ImGui.SliderFloat("Display Time (s)", ref displayTime, ConfigStatic.MinDisplayTime, ConfigStatic.MaxDisplayTime))
            {
                this.config.DisplayTimeSeconds = displayTime;
                changed = true;
            }

            var iconScale = this.config.IconScale;
            if (ImGui.SliderFloat("Icon Scale", ref iconScale, 0.5f, 3.0f))
            {
                this.config.IconScale = iconScale;
                changed = true;
            }

            var bg = this.config.BackgroundColor;
            if (ImGui.ColorEdit4("Background Color", ref bg))
            {
                this.config.BackgroundColor = bg;
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var showCastBars = this.config.ShowCastBars;
            if (ImGui.Checkbox("Show Cast Bars", ref showCastBars))
            {
                this.config.ShowCastBars = showCastBars;
                changed = true;
            }

            var showGrid = this.config.ShowGrid;
            if (ImGui.Checkbox("Show Grid Lines", ref showGrid))
            {
                this.config.ShowGrid = showGrid;
                changed = true;
            }

            var showHGrid = this.config.ShowHorizontalGridLines;
            if (ImGui.Checkbox("Show Horizontal Grid Lines", ref showHGrid))
            {
                this.config.ShowHorizontalGridLines = showHGrid;
                changed = true;
            }

            var showRowLabels = this.config.ShowRowLabels;
            if (ImGui.Checkbox("Show Row Labels", ref showRowLabels))
            {
                this.config.ShowRowLabels = showRowLabels;
                changed = true;
            }

            var showTitleBar = this.config.ShowTitleBar;
            if (ImGui.Checkbox("Show Title Bar", ref showTitleBar))
            {
                this.config.ShowTitleBar = showTitleBar;
                changed = true;
            }

            var disableEsc = this.config.DisableEscClose;
            if (ImGui.Checkbox("Ignore ESC Key", ref disableEsc))
            {
                this.config.DisableEscClose = disableEsc;
                changed = true;
            }

            var hideOoc = this.config.HideOutOfCombat;
            if (ImGui.Checkbox("Hide Out of Combat", ref hideOoc))
            {
                this.config.HideOutOfCombat = hideOoc;
                changed = true;
            }

            if (hideOoc)
            {
                ImGui.Indent();
                var delay = this.config.HideOutOfCombatDelay;
                if (ImGui.SliderFloat("Hide Delay (s)", ref delay, 0f, 30f))
                {
                    this.config.HideOutOfCombatDelay = delay;
                    changed = true;
                }

                ImGui.Unindent();
            }

#if DEBUG
            var showDebug = this.config.ShowDebugInfo;
            if (ImGui.Checkbox("Show Debug Info", ref showDebug))
            {
                this.config.ShowDebugInfo = showDebug;
                changed = true;
            }
#endif
        }

        if (ImGui.CollapsingHeader("Row Layout", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted("Toggle visibility, reorder rows, and adjust height.");
            ImGui.Spacing();

            // Deduplicate row order in case saved config has duplicates
            var order = this.config.RowOrder;
            var seen = new HashSet<ActionType>();
            for (var j = order.Count - 1; j >= 0; j--)
            {
                if (!seen.Add(order[j]))
                    order.RemoveAt(j);
            }

            var heights = this.config.RowHeights;
            int? swapFrom = null;
            int? swapTo = null;

            for (var i = 0; i < order.Count; i++)
            {
                var rowType = order[i];
                var label = GetRowLabel(rowType);

                ImGui.PushID(i);

                // Visibility checkbox
                var visible = this.GetRowVisible(rowType);
                if (ImGui.Checkbox("##visible", ref visible))
                {
                    this.SetRowVisible(rowType, visible);
                    changed = true;
                }

                ImGui.SameLine();

                // Move up button
                var canUp = i > 0;
                if (!canUp) ImGui.BeginDisabled();
                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                {
                    swapFrom = i;
                    swapTo = i - 1;
                }

                if (!canUp) ImGui.EndDisabled();

                ImGui.SameLine();

                // Move down button
                var canDown = i < order.Count - 1;
                if (!canDown) ImGui.BeginDisabled();
                if (ImGui.ArrowButton("##down", ImGuiDir.Down))
                {
                    swapFrom = i;
                    swapTo = i + 1;
                }

                if (!canDown) ImGui.EndDisabled();

                ImGui.SameLine();
                ImGui.TextUnformatted(label);
                ImGui.SameLine();

                ImGui.SetNextItemWidth(120);
                if (!heights.TryGetValue(rowType, out var h))
                    h = 1.0f;

                if (ImGui.SliderFloat("##height", ref h, 0.25f, 3.0f, "%.2f"))
                {
                    heights[rowType] = h;
                    changed = true;
                }

                ImGui.PopID();
            }

            if (swapFrom is not null && swapTo is not null)
            {
                (order[swapFrom.Value], order[swapTo.Value]) = (order[swapTo.Value], order[swapFrom.Value]);
                changed = true;
            }
        }

        if (changed)
            this.pluginInterface.SavePluginConfig(this.config);
    }

    private static string GetRowLabel(ActionType type) => type switch
    {
        ActionType.AutoAttack => "Auto-Attack",
        ActionType.Gcd => "GCD",
        ActionType.Ogcd => "oGCD",
        ActionType.Pet => "Pet",
        _ => "Unknown",
    };

    private bool GetRowVisible(ActionType type) => type switch
    {
        ActionType.Gcd => this.config.ShowGcd,
        ActionType.Ogcd => this.config.ShowOgcd,
        ActionType.AutoAttack => this.config.ShowAutoAttack,
        ActionType.Pet => this.config.ShowPet,
        _ => true,
    };

    private void SetRowVisible(ActionType type, bool visible)
    {
        switch (type)
        {
            case ActionType.Gcd: this.config.ShowGcd = visible; break;
            case ActionType.Ogcd: this.config.ShowOgcd = visible; break;
            case ActionType.AutoAttack: this.config.ShowAutoAttack = visible; break;
            case ActionType.Pet: this.config.ShowPet = visible; break;
        }
    }
}
