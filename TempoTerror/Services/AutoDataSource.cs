namespace TempoTerror.Services;

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

/// <summary>
/// Tries IPC first; on failure, silently falls back to WebSocket.
/// </summary>
public sealed class AutoDataSource : IDataSource
{
    private readonly IpcDataSource ipcSource;
    private readonly WebSocketDataSource wsSource;
    private readonly IPluginLog log;
    private IDataSource? activeSource;
    private bool ipcFailed;

    public event Action<string>? OnLogLine;

    public bool IsConnected => this.activeSource?.IsConnected ?? false;

    public string ConnectionStatus { get; private set; } = "Connecting...";

    public AutoDataSource(IDalamudPluginInterface pluginInterface, string webSocketUrl, IPluginLog log)
    {
        this.log = log;
        this.ipcSource = new IpcDataSource(pluginInterface, log);
        this.wsSource = new WebSocketDataSource(webSocketUrl, log);

        this.ipcSource.OnLogLine += this.ForwardLogLine;
        this.wsSource.OnLogLine += this.ForwardLogLine;
    }

    public void Connect()
    {
        // If already connected via an active source, nothing to do.
        if (this.activeSource is { IsConnected: true })
        {
            this.ConnectionStatus = this.activeSource.ConnectionStatus;
            return;
        }

        // Try IPC first (unless it already failed once).
        if (!this.ipcFailed)
        {
            this.ConnectionStatus = "Connecting via IPC...";
            this.ipcSource.Connect();

            if (this.ipcSource.IsConnected)
            {
                this.activeSource = this.ipcSource;
                this.ConnectionStatus = this.ipcSource.ConnectionStatus;
                this.log.Information("[TempoTerror] Auto: using IPC data source.");
                return;
            }

            // IPC did not connect — mark it as failed so we don't keep
            // retrying IPC every framework tick while WebSocket is active.
            this.ipcFailed = true;
            this.log.Information("[TempoTerror] Auto: IPC unavailable, falling back to WebSocket.");
        }

        // Fall back to WebSocket.
        this.ConnectionStatus = "Connecting via WebSocket...";
        this.wsSource.Connect();

        if (this.wsSource.IsConnected)
        {
            this.activeSource = this.wsSource;
            this.ConnectionStatus = this.wsSource.ConnectionStatus;
        }
        else
        {
            this.ConnectionStatus = "Waiting for IINACT...";
        }
    }

    public void Dispose()
    {
        this.ipcSource.OnLogLine -= this.ForwardLogLine;
        this.wsSource.OnLogLine -= this.ForwardLogLine;

        this.ipcSource.Dispose();
        this.wsSource.Dispose();
        this.activeSource = null;
    }

    private void ForwardLogLine(string line)
    {
        this.OnLogLine?.Invoke(line);
    }
}
