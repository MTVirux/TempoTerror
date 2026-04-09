namespace TempoTerror.Services;

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

public sealed class DataService : IDisposable
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> createSubscriber;
    private readonly ICallGateSubscriber<string, bool> unsubscribe;
    private readonly ICallGateSubscriber<JObject, bool> provider;
    private bool subscribed;

    public event Action<string>? OnLogLine;

    public bool IsConnected => this.subscribed;

    public DataService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        this.createSubscriber = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.CreateSubscriber");
        this.unsubscribe = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.Unsubscribe");
        this.provider = pluginInterface.GetIpcSubscriber<JObject, bool>(
            $"IINACT.IpcProvider.{ConfigStatic.SubscriberName}");

        this.provider.Subscribe(this.OnIpcMessage);
    }

    public void Connect()
    {
        if (this.subscribed)
            return;

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
            this.subscribed = true;

            var subscribeMsg = new JObject
            {
                ["call"] = "subscribe",
                ["events"] = new JArray("LogLine"),
            };
            this.provider.InvokeFunc(subscribeMsg);

            this.log.Information("[TempoTerror] IINACT subscription active.");
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[TempoTerror] Failed to connect to IINACT. Is IINACT running?");
            this.subscribed = false;
        }
    }

    public void Dispose()
    {
        this.provider.Unsubscribe(this.OnIpcMessage);

        if (this.subscribed)
        {
            try
            {
                this.unsubscribe.InvokeFunc(ConfigStatic.SubscriberName);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "[TempoTerror] Failed to unsubscribe from IINACT.");
            }

            this.subscribed = false;
        }
    }

    private void OnIpcMessage(JObject data)
    {
        var msgType = data["type"]?.ToString();
        if (msgType != "LogLine")
            return;

        var rawLine = data["rawLine"]?.ToString();
        if (rawLine is not null)
            this.OnLogLine?.Invoke(rawLine);
    }
}
