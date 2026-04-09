namespace TempoTerror.Gui.ConfigWindow;

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using TempoTerror.Models;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;

    public ConfigWindow(Configuration config, IDalamudPluginInterface pluginInterface)
        : base("Tempo Terror Settings##ConfigWindow")
    {
        this.config = config;
        this.pluginInterface = pluginInterface;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350, 300),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

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
