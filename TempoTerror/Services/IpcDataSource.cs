namespace TempoTerror.Services;

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

public sealed class IpcDataSource : IDataSource
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> createSubscriber;
    private readonly ICallGateSubscriber<string, bool> unsubscribe;
    private readonly ICallGateProvider<JObject, bool> receiver;
    private readonly ICallGateSubscriber<JObject, bool> sender;
    private bool connected;
    private DateTime nextRetry = DateTime.MinValue;

    public event Action<string>? OnLogLine;

    public bool IsConnected => this.connected;

    public IpcDataSource(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        this.createSubscriber = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.CreateSubscriber");
        this.unsubscribe = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.Unsubscribe");

        this.receiver = pluginInterface.GetIpcProvider<JObject, bool>(
            $"IINACT.IpcProvider.{ConfigStatic.SubscriberName}");
        this.receiver.RegisterFunc(this.OnDataReceived);

        this.sender = pluginInterface.GetIpcSubscriber<JObject, bool>(ConfigStatic.SubscriberName);
    }

    public void Connect()
    {
        if (this.connected)
            return;

        var now = DateTime.UtcNow;
        if (now < this.nextRetry)
            return;

        this.nextRetry = now.AddSeconds(5);

        try
        {
            // Clean up any stale subscriber from a previous session (e.g. plugin reload).
            try
            {
                this.unsubscribe.InvokeFunc(ConfigStatic.SubscriberName);
            }
            catch
            {
                // Ignore — no prior subscriber existed.
            }

            this.createSubscriber.InvokeFunc(ConfigStatic.SubscriberName);

            var subscribeMsg = new JObject
            {
                ["call"] = "subscribe",
                ["events"] = new JArray("LogLine"),
            };
            this.sender.InvokeFunc(subscribeMsg);
            this.connected = true;

            this.log.Information("[TempoTerror] IINACT subscription active.");
        }
        catch (Exception ex)
        {
            this.log.Debug("[TempoTerror] IINACT not ready yet: {Message}", ex.Message);
            this.connected = false;
        }
    }

    public void Dispose()
    {
        this.receiver.UnregisterFunc();

        if (this.connected)
        {
            try
            {
                this.unsubscribe.InvokeFunc(ConfigStatic.SubscriberName);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        this.connected = false;
    }

    private bool OnDataReceived(JObject data)
    {
        var msgType = data["type"]?.ToString();
        if (msgType != "LogLine")
            return true;

        var rawLine = data["rawLine"]?.ToString();
        if (rawLine is not null)
            this.OnLogLine?.Invoke(rawLine);

        return true;
    }
}
