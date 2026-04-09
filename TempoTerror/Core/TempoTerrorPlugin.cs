namespace TempoTerror.Core;

using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TempoTerror.Models;
using TempoTerror.Services;

public sealed class TempoTerrorPlugin : IDalamudPlugin, IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly WindowSystem windowSystem;
    private readonly Configuration config;
    private readonly IDataSource dataSource;
    private readonly ActionTracker actionTracker;
    private readonly IconCache iconCache;
    private readonly Gui.MainWindow.MainWindow mainWindow;
    private readonly Gui.ConfigWindow.ConfigWindow configWindow;

    public TempoTerrorPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IObjectTable objectTable,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        IPluginLog log,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;

        var cfg = this.pluginInterface.GetPluginConfig() as Configuration;
        if (cfg is null)
        {
            cfg = new Configuration();
            this.pluginInterface.SavePluginConfig(cfg);
        }

        this.config = cfg;

        // Services
        this.dataSource = this.config.DataSourceMode switch
        {
            DataSourceMode.WebSocket => new WebSocketDataSource(this.config.WebSocketUrl, log),
            _ => new IpcDataSource(pluginInterface, log),
        };
        this.actionTracker = new ActionTracker(objectTable, dataManager, log, this.dataSource);
        this.iconCache = new IconCache(textureProvider, dataManager);

        // Windows
        this.windowSystem = new WindowSystem("TempoTerror");
        this.configWindow = new Gui.ConfigWindow.ConfigWindow(this.config, pluginInterface, this.actionTracker);
        this.mainWindow = new Gui.MainWindow.MainWindow(this.config, pluginInterface, this.actionTracker, this.iconCache, this.configWindow);

        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        // Events
        this.pluginInterface.UiBuilder.Draw += this.DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        this.framework.Update += this.OnFrameworkUpdate;

        // Command
        this.commandManager.AddHandler(ConfigStatic.CommandName, new Dalamud.Game.Command.CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the TempoTerror timeline window.",
        });

        // Connect to IINACT
        this.dataSource.Connect();

        if (this.config.ShowOnStart)
            this.mainWindow.IsOpen = true;
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(ConfigStatic.CommandName);
        this.framework.Update -= this.OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
        this.pluginInterface.UiBuilder.Draw -= this.DrawUi;

        this.windowSystem.RemoveAllWindows();
        this.mainWindow.Dispose();
        this.configWindow.Dispose();

        this.actionTracker.Dispose();
        this.dataSource.Dispose();
    }

    private void DrawUi() => this.windowSystem.Draw();

    private void OpenMainUi() => this.mainWindow.IsOpen = true;

    private void OpenConfigUi() => this.configWindow.IsOpen = true;

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!this.dataSource.IsConnected)
            this.dataSource.Connect();

        this.actionTracker.ProcessPendingLines();
        this.actionTracker.Prune(this.config.DisplayTimeSeconds);
    }

    private void OnCommand(string command, string args)
    {
        this.mainWindow.IsOpen = !this.mainWindow.IsOpen;
    }
}
