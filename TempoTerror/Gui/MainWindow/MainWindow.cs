namespace TempoTerror.Gui.MainWindow;

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface.Windowing;
using TempoTerror.Models;
using TempoTerror.Services;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly ActionTracker tracker;
    private readonly IconCache iconCache;

    private uint displayPlayerId;

    public MainWindow(Configuration config, ActionTracker tracker, IconCache iconCache)
        : base("Tempo Terror##MainWindow")
    {
        this.config = config;
        this.tracker = tracker;
        this.iconCache = iconCache;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 200),
            MaximumSize = new Vector2(2000, 600),
        };
        this.Size = new Vector2(800, 250);
        this.Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        this.DrawPlayerSelector();
        this.DrawTimeline();
    }

    private void DrawPlayerSelector()
    {
        var localId = this.tracker.LocalPlayerId;
        var selectedId = this.config.SelectedPlayerId ?? localId;
        this.displayPlayerId = selectedId;

        var actors = this.tracker.KnownActors;
        var currentLabel = actors.TryGetValue(selectedId, out var name) ? name : "Local Player";

        if (ImGui.BeginCombo("Player", currentLabel))
        {
            // Local player option
            var localName = actors.TryGetValue(localId, out var ln) ? ln : "Local Player";
            if (ImGui.Selectable(localName, selectedId == localId))
                this.config.SelectedPlayerId = null;

            // Other tracked actors
            foreach (var kvp in actors)
            {
                if (kvp.Key == localId)
                    continue;

                if (ImGui.Selectable(kvp.Value, selectedId == kvp.Key))
                    this.config.SelectedPlayerId = kvp.Key;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawTimeline()
    {
        var now = this.tracker.CurrentTime;
        var displayTime = this.config.DisplayTimeSeconds;
        var xMin = now - displayTime;
        var xMax = now;

        var iconSize = ConfigStatic.DefaultIconSize * this.config.IconScale;

        if (!ImPlot.BeginPlot("##Timeline", new Vector2(-1, -1), ImPlotFlags.NoLegend | ImPlotFlags.NoMouseText))
            return;

        ImPlot.SetupAxes("Time", "Type",
            ImPlotAxisFlags.NoTickLabels | ImPlotAxisFlags.NoGridLines,
            ImPlotAxisFlags.NoTickLabels | ImPlotAxisFlags.NoGridLines | ImPlotAxisFlags.Invert);

        ImPlot.SetupAxisLimits(ImAxis.X1, xMin, xMax, ImPlotCond.Always);
        ImPlot.SetupAxisLimits(ImAxis.Y1, -0.5, 3.5, ImPlotCond.Always);

        // Row labels
        ImPlot.PlotText("Auto", xMin + displayTime * 0.02, 0);
        ImPlot.PlotText("GCD", xMin + displayTime * 0.02, 1);
        ImPlot.PlotText("oGCD", xMin + displayTime * 0.02, 2);
        ImPlot.PlotText("Pet", xMin + displayTime * 0.02, 3);

        // Draw horizontal row separators
        var drawList = ImPlot.GetPlotDrawList();
        var separatorColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

        for (var row = 0; row < 4; row++)
        {
            var leftPx = ImPlot.PlotToPixels(xMin, row - 0.5);
            var rightPx = ImPlot.PlotToPixels(xMax, row - 0.5);
            drawList.AddLine(leftPx, rightPx, separatorColor);
        }

        // Get actions for the selected player
        var playerId = this.displayPlayerId != 0 ? this.displayPlayerId : this.tracker.LocalPlayerId;
        var actions = this.tracker.GetActions(playerId);

        this.DrawActions(drawList, actions, iconSize, xMin, xMax);

        ImPlot.EndPlot();
    }

    private void DrawActions(ImDrawListPtr drawList, List<TrackedAction> actions, float iconSize, double xMin, double xMax)
    {
        var halfIcon = iconSize * 0.5f;

        foreach (var action in actions)
        {
            if (action.Timestamp < xMin || action.Timestamp > xMax)
                continue;

            if (!this.ShouldShow(action.ActionType))
                continue;

            var row = GetRow(action.ActionType);
            var center = ImPlot.PlotToPixels(action.Timestamp, row);

            // Draw cast bar behind the icon
            if (this.config.ShowCastBars && action.CastTime > 0)
                this.DrawCastBar(drawList, action, row, halfIcon);

            // Draw action icon
            var iconWrap = this.iconCache.GetIconWrap(action.ActionId, action.IconId);
            if (iconWrap is not null)
            {
                var pMin = new Vector2(center.X - halfIcon, center.Y - halfIcon);
                var pMax = new Vector2(center.X + halfIcon, center.Y + halfIcon);
                drawList.AddImage(iconWrap.Handle, pMin, pMax);
            }

            // Draw cancelled overlay
            if (action.WasCancelled)
            {
                var cancelColor = ImGui.GetColorU32(new Vector4(1f, 0.2f, 0.2f, 0.8f));
                var topLeft = new Vector2(center.X - halfIcon, center.Y - halfIcon);
                var bottomRight = new Vector2(center.X + halfIcon, center.Y + halfIcon);
                drawList.AddLine(topLeft, bottomRight, cancelColor, 3f);
                drawList.AddLine(
                    new Vector2(topLeft.X, bottomRight.Y),
                    new Vector2(bottomRight.X, topLeft.Y),
                    cancelColor, 3f);
            }
        }
    }

    private void DrawCastBar(ImDrawListPtr drawList, TrackedAction action, double row, float halfIcon)
    {
        var castEnd = action.Timestamp + action.CastTime;
        var barStart = ImPlot.PlotToPixels(action.Timestamp, row);
        var barEnd = ImPlot.PlotToPixels(castEnd, row);

        var castColor = action.WasCancelled
            ? ImGui.GetColorU32(new Vector4(0.8f, 0.2f, 0.2f, 0.3f))
            : action.IsCasting
                ? ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 1.0f, 0.3f))
                : ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 0.3f));

        drawList.AddRectFilled(
            new Vector2(barStart.X, barStart.Y - halfIcon),
            new Vector2(barEnd.X, barEnd.Y + halfIcon),
            castColor);
    }

    private bool ShouldShow(ActionType type) => type switch
    {
        ActionType.Gcd => this.config.ShowGcd,
        ActionType.Ogcd => this.config.ShowOgcd,
        ActionType.AutoAttack => this.config.ShowAutoAttack,
        ActionType.Pet => this.config.ShowPet,
        _ => true,
    };

    private static double GetRow(ActionType type) => type switch
    {
        ActionType.AutoAttack => 0,
        ActionType.Gcd => 1,
        ActionType.Ogcd => 2,
        ActionType.Pet => 3,
        _ => 1,
    };
}
