namespace TempoTerror.Services;

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

public sealed class IinactSubscriber : IDisposable
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, bool> createSubscriber;
    private readonly ICallGateSubscriber<string, bool> unsubscribe;
    private readonly ICallGateSubscriber<JObject, bool> provider;
    private bool subscribed;

    public event Action<string>? OnLogLine;

    public bool IsConnected => this.subscribed;

    public IinactSubscriber(IDalamudPluginInterface pluginInterface, IPluginLog log)
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
            this.createSubscriber.InvokeFunc(ConfigStatic.SubscriberName);
            this.subscribed = true;

            var subscribeMsg = new JObject
            {
                ["call"] = "subscribe",
                ["events"] = new JArray("LogLine"),
            };
            this.provider.InvokeFunc(subscribeMsg);

            this.log.Information("IINACT subscription active.");
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Failed to connect to IINACT. Is IINACT running?");
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
                this.log.Warning(ex, "Failed to unsubscribe from IINACT.");
            }

            this.subscribed = false;
        }
    }

    private void OnIpcMessage(JObject data)
    {
        var msgType = data["msgtype"]?.ToString();
        if (msgType != "N" && msgType != "LogLine")
            return;

        var msg = data["msg"]?.ToString();
        if (msg is not null)
            this.OnLogLine?.Invoke(msg);
    }
}
