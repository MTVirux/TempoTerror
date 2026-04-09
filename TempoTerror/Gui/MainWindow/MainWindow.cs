namespace TempoTerror.Gui.MainWindow;

using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using TempoTerror.Models;
using TempoTerror.Services;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ActionTracker tracker;
    private readonly IconCache iconCache;
    private readonly Window configWindow;
    private readonly TitleBarButton lockButton;

    private uint displayPlayerId;

    private Vector2 plotOrigin;
    private Vector2 plotSize;
    private double axisXMin;
    private double axisXMax;
    private double axisYMin;
    private double axisYMax;

    public MainWindow(Configuration config, IDalamudPluginInterface pluginInterface, ActionTracker tracker, IconCache iconCache, Window configWindow)
        : base("TempoTerror##MainWindow")
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.tracker = tracker;
        this.iconCache = iconCache;
        this.configWindow = configWindow;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(50, 50),
            MaximumSize = new Vector2(2000, 600),
        };
        this.Size = new Vector2(800, 250);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground;

        this.lockButton = new TitleBarButton
        {
            Icon = this.config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.Unlock,
            IconOffset = new Vector2(2, 2),
            Click = _ =>
            {
                this.config.PinMainWindow = !this.config.PinMainWindow;
                this.lockButton!.Icon = this.config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.Unlock;
                this.pluginInterface.SavePluginConfig(this.config);
            },
            ShowTooltip = () => ImGui.SetTooltip(this.config.PinMainWindow ? "Unlock Window" : "Lock Window"),
        };

        this.TitleBarButtons.Add(this.lockButton);
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 2),
            Click = _ => this.configWindow.IsOpen = !this.configWindow.IsOpen,
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (this.config.PinMainWindow)
            this.Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        else
            this.Flags &= ~(ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);

        if (this.config.ShowTitleBar)
            this.Flags &= ~ImGuiWindowFlags.NoTitleBar;
        else
            this.Flags |= ImGuiWindowFlags.NoTitleBar;

        this.RespectCloseHotkey = !this.config.DisableEscClose;
    }

    public override void Draw()
    {
        this.displayPlayerId = this.config.SelectedPlayerId ?? this.tracker.LocalPlayerId;
#if DEBUG
        if (this.config.ShowDebugInfo)
            this.DrawDebugInfo();
#endif
        this.DrawTimeline();
    }

    private void DrawDebugInfo()
    {
        var playerId = this.displayPlayerId != 0 ? this.displayPlayerId : this.tracker.LocalPlayerId;
        var actions = this.tracker.GetActions(playerId);
        var gcd = 0;
        var ogcd = 0;
        var auto = 0;
        var pet = 0;
        foreach (var a in actions)
        {
            switch (a.ActionType)
            {
                case ActionType.Gcd: gcd++; break;
                case ActionType.Ogcd: ogcd++; break;
                case ActionType.AutoAttack: auto++; break;
                case ActionType.Pet: pet++; break;
            }
        }

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
            $"[Debug] Player:{playerId:X} GCD:{gcd} oGCD:{ogcd} Auto:{auto} Pet:{pet} Total:{actions.Count}");
    }

    private Vector2 MapToScreen(double x, double y)
    {
        var sx = this.plotOrigin.X + (float)((x - this.axisXMin) / (this.axisXMax - this.axisXMin)) * this.plotSize.X;
        var sy = this.plotOrigin.Y + (float)((y - this.axisYMin) / (this.axisYMax - this.axisYMin)) * this.plotSize.Y;
        return new Vector2(sx, sy);
    }

    private void DrawTimeline()
    {
        var now = this.tracker.CurrentTime;
        var displayTime = this.config.DisplayTimeSeconds;
        var xMin = now - displayTime;
        var xMax = now;

        var iconSize = ConfigStatic.DefaultIconSize * this.config.IconScale;

        // Build row layout from config
        var rowOrder = this.config.RowOrder;
        var visibleRows = new List<(ActionType Type, string Label)>();
        foreach (var type in rowOrder)
        {
            if (this.ShouldShow(type))
                visibleRows.Add((type, GetRowLabel(type)));
        }

        if (visibleRows.Count == 0)
            return;

        // Compute cumulative Y positions based on row heights
        var rowPositions = new Dictionary<ActionType, double>();
        var y = 0.0;
        foreach (var (type, _) in visibleRows)
        {
            var height = this.config.RowHeights.TryGetValue(type, out var h) ? h : 1.0f;
            rowPositions[type] = y + height * 0.5;
            y += height;
        }

        var totalHeight = y;

        // Set up axis bounds
        this.axisXMin = xMin;
        this.axisXMax = xMax;
        this.axisYMin = -0.25;
        this.axisYMax = totalHeight + 0.25;

        if (!ImGui.BeginChild("##Timeline", new Vector2(-1, -1), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.EndChild();
            return;
        }

        this.plotOrigin = ImGui.GetCursorScreenPos();
        this.plotSize = ImGui.GetContentRegionAvail();

        var drawList = ImGui.GetWindowDrawList();
        var regionMax = this.plotOrigin + this.plotSize;

        // Background
        var bgColor = ImGui.GetColorU32(this.config.BackgroundColor);
        drawList.AddRectFilled(this.plotOrigin, regionMax, bgColor);

        // Clip to plot region
        drawList.PushClipRect(this.plotOrigin, regionMax, true);

        // Grid lines
        if (this.config.ShowGrid)
            this.DrawGridLines(drawList, xMin, xMax, displayTime);

        // Row labels
        if (this.config.ShowRowLabels)
        {
            var labelColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f));
            foreach (var (type, label) in visibleRows)
            {
                var labelPos = this.MapToScreen(xMin + displayTime * 0.02, rowPositions[type]);
                var textSize = ImGui.CalcTextSize(label);
                labelPos.Y -= textSize.Y * 0.5f;
                drawList.AddText(labelPos, labelColor, label);
            }
        }

        // Draw horizontal row separators
        if (this.config.ShowHorizontalGridLines)
        {
            var separatorColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            var sepY = 0.0;
            for (var i = 0; i < visibleRows.Count; i++)
            {
                var leftPx = this.MapToScreen(xMin, sepY);
                var rightPx = this.MapToScreen(xMax, sepY);
                drawList.AddLine(leftPx, rightPx, separatorColor);

                var height = this.config.RowHeights.TryGetValue(visibleRows[i].Type, out var h) ? h : 1.0f;
                sepY += height;
            }

            // Bottom separator
            {
                var leftPx = this.MapToScreen(xMin, sepY);
                var rightPx = this.MapToScreen(xMax, sepY);
                drawList.AddLine(leftPx, rightPx, separatorColor);
            }
        }

        // Get actions for the selected player
        var playerId = this.displayPlayerId != 0 ? this.displayPlayerId : this.tracker.LocalPlayerId;
        var actions = this.tracker.GetActions(playerId);

        this.DrawActions(drawList, actions, iconSize, xMin, xMax, rowPositions, this.config.RowHeights);

        drawList.PopClipRect();

        if (ImGui.BeginPopupContextWindow("##TimelineContext"))
        {
            var lockLabel = this.config.PinMainWindow ? "Unlock Window" : "Lock Window";
            if (ImGui.MenuItem(lockLabel))
            {
                this.config.PinMainWindow = !this.config.PinMainWindow;
                this.lockButton.Icon = this.config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.Unlock;
                this.pluginInterface.SavePluginConfig(this.config);
            }

            if (ImGui.MenuItem("Settings"))
                this.configWindow.IsOpen = !this.configWindow.IsOpen;

            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }

    private void DrawGridLines(ImDrawListPtr drawList, double xMin, double xMax, float displayTime)
    {
        var gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.3f));

        var interval = displayTime switch
        {
            <= 5f => 1.0,
            <= 15f => 2.0,
            _ => 5.0,
        };

        var firstTick = Math.Ceiling(xMin / interval) * interval;
        for (var t = firstTick; t <= xMax; t += interval)
        {
            var top = this.MapToScreen(t, this.axisYMin);
            var bottom = this.MapToScreen(t, this.axisYMax);
            drawList.AddLine(top, bottom, gridColor);
        }
    }

    private void DrawActions(ImDrawListPtr drawList, List<TrackedAction> actions, float iconSize, double xMin, double xMax, Dictionary<ActionType, double> rowPositions, Dictionary<ActionType, float> rowHeights)
    {
        foreach (var action in actions)
        {
            if (action.Timestamp < xMin || action.Timestamp > xMax)
                continue;

            if (!rowPositions.TryGetValue(action.ActionType, out var row))
                continue;

            var rowHeight = rowHeights.TryGetValue(action.ActionType, out var h) ? h : 1.0f;
            var scaledIconSize = iconSize * rowHeight;
            var halfIcon = scaledIconSize * 0.5f;

            var center = this.MapToScreen(action.Timestamp, row);

            // Draw cast bar behind the icon
            if (this.config.ShowCastBars && action.CastTime > 0)
                this.DrawCastBar(drawList, action, row, halfIcon);

            // Draw action icon
            var pMin = new Vector2(center.X - halfIcon, center.Y - halfIcon);
            var pMax = new Vector2(center.X + halfIcon, center.Y + halfIcon);
            var iconWrap = this.iconCache.GetIconWrap(action.ActionId, action.IconId);
            if (iconWrap is not null)
            {
                drawList.AddImage(iconWrap.Handle, pMin, pMax);
            }
            else
            {
                var fallback = action.ActionType switch
                {
                    ActionType.Gcd => new Vector4(0.3f, 0.7f, 1.0f, 0.7f),
                    ActionType.Ogcd => new Vector4(1.0f, 0.6f, 0.2f, 0.7f),
                    ActionType.AutoAttack => new Vector4(0.7f, 0.7f, 0.7f, 0.7f),
                    ActionType.Pet => new Vector4(0.4f, 0.9f, 0.4f, 0.7f),
                    _ => new Vector4(1f, 1f, 1f, 0.5f),
                };
                drawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(fallback));
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
        var barStart = this.MapToScreen(action.Timestamp, row);
        var barEnd = this.MapToScreen(castEnd, row);

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

    private static string GetRowLabel(ActionType type) => type switch
    {
        ActionType.AutoAttack => "Auto",
        ActionType.Gcd => "GCD",
        ActionType.Ogcd => "oGCD",
        ActionType.Pet => "Pet",
        _ => "?",
    };
}
