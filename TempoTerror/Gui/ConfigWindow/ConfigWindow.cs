namespace TempoTerror.Gui.ConfigWindow;

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

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

        if (ImGui.CollapsingHeader("Action Filters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var showGcd = this.config.ShowGcd;
            if (ImGui.Checkbox("Show GCD", ref showGcd))
            {
                this.config.ShowGcd = showGcd;
                changed = true;
            }

            var showOgcd = this.config.ShowOgcd;
            if (ImGui.Checkbox("Show oGCD", ref showOgcd))
            {
                this.config.ShowOgcd = showOgcd;
                changed = true;
            }

            var showPet = this.config.ShowPet;
            if (ImGui.Checkbox("Show Pet", ref showPet))
            {
                this.config.ShowPet = showPet;
                changed = true;
            }

            var showAutoAttack = this.config.ShowAutoAttack;
            if (ImGui.Checkbox("Show Auto-Attack", ref showAutoAttack))
            {
                this.config.ShowAutoAttack = showAutoAttack;
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

        if (changed)
            this.pluginInterface.SavePluginConfig(this.config);
    }
}
