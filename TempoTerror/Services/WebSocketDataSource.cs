namespace TempoTerror.Services;

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class WebSocketDataSource : IDataSource
{
    private readonly IPluginLog log;
    private readonly string url;
    private ClientWebSocket? ws;
    private CancellationTokenSource? cts;
    private Task? receiveTask;
    private volatile bool disposed;

    public event Action<string>? OnLogLine;

    public bool IsConnected => this.ws?.State == WebSocketState.Open;

    public string ConnectionStatus { get; private set; } = "Not connected";

    public WebSocketDataSource(string url, IPluginLog log)
    {
        this.url = url;
        this.log = log;
    }

    public void Connect()
    {
        if (this.disposed || this.IsConnected || this.receiveTask is not null)
            return;

        this.cts = new CancellationTokenSource();
        this.receiveTask = Task.Run(() => this.ConnectAndReceiveLoopAsync(this.cts.Token));
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        try
        {
            this.cts?.Cancel();
            if (this.ws?.State == WebSocketState.Open)
            {
                this.ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Plugin closing", CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
        finally
        {
            this.ws?.Dispose();
            this.ws = null;
            this.cts?.Dispose();
        }
    }

    private async Task ConnectAndReceiveLoopAsync(CancellationToken ct)
    {
        var retryDelay = 1000;
        const int maxDelay = 30_000;

        while (!ct.IsCancellationRequested && !this.disposed)
        {
            try
            {
                await this.ConnectOnceAsync(ct).ConfigureAwait(false);
                retryDelay = 1000;
                await this.ReceiveLoopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.log.Debug("[TempoTerror] WebSocket error: {Message}", ex.Message);
                this.ConnectionStatus = "WebSocket error";
            }

            if (ct.IsCancellationRequested || this.disposed)
                break;

            this.log.Debug("[TempoTerror] WebSocket reconnecting in {Delay}ms...", retryDelay);
            this.ConnectionStatus = "WebSocket reconnecting...";

            try
            {
                await Task.Delay(retryDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            retryDelay = Math.Min(retryDelay * 2, maxDelay);
        }
    }

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        this.ws?.Dispose();
        this.ws = new ClientWebSocket();

        await this.ws.ConnectAsync(new Uri(this.url), ct).ConfigureAwait(false);
        this.log.Information("[TempoTerror] WebSocket connected to {Url}", this.url);
        this.ConnectionStatus = "Connected (WebSocket)";

        var subscribeMsg = JsonConvert.SerializeObject(new
        {
            call = "subscribe",
            events = new[] { "LogLine" },
        });
        var bytes = Encoding.UTF8.GetBytes(subscribeMsg);
        await this.ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && this.ws?.State == WebSocketState.Open)
        {
            messageBuilder.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await this.ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.log.Information("[TempoTerror] WebSocket server closed connection.");
                    return;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            this.ProcessMessage(messageBuilder.ToString());
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var data = JObject.Parse(message);
            var msgType = data["type"]?.ToString();
            if (msgType != "LogLine")
                return;

            var rawLine = data["rawLine"]?.ToString();
            if (rawLine is not null)
                this.OnLogLine?.Invoke(rawLine);
        }
        catch (JsonException ex)
        {
            this.log.Debug("[TempoTerror] Failed to parse WebSocket message: {Message}", ex.Message);
        }
    }
}
