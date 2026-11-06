using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nyautomator.Govee;

public sealed class GoveeEventStream : IAsyncDisposable
{
    private static readonly Uri DefaultEndpoint = new("wss://openapi.api.govee.com/socket");
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorLogCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReconnectLogCooldown = TimeSpan.FromMinutes(1);

    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly TimeSpan _reconnectDelay;
    private readonly object _gate = new();
    private readonly object _logGate = new();

    private DateTimeOffset _lastSocketErrorLogUtc = DateTimeOffset.MinValue;
    private int _socketErrorSuppressed;
    private DateTimeOffset _lastReconnectLogUtc = DateTimeOffset.MinValue;
    private int _reconnectSuppressed;

    private CancellationTokenSource? _cts;
    private Task? _runner;

    public event EventHandler<GoveeDeviceEvent>? DeviceEventReceived;

    public GoveeEventStream(string apiKey, Uri? endpoint = null, TimeSpan? reconnectDelay = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required", nameof(apiKey));

        _apiKey = apiKey.Trim();
        _endpoint = endpoint ?? DefaultEndpoint;
        _reconnectDelay = reconnectDelay ?? DefaultReconnectDelay;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_runner is not null)
                return;

            _cts = new CancellationTokenSource();
            _runner = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? runner;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            runner = _runner;
            cts = _cts;
            _runner = null;
            _cts = null;
        }

        if (cts is not null)
            cts.Cancel();

        if (runner is not null)
        {
            try { await runner.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine($"[Govee] Event stream shutdown error: {ex.Message}"); }
        }

        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken token)
    {
        Console.WriteLine("[Govee] Event stream starting.");

        while (!token.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Govee-API-Key", _apiKey);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            try
            {
                await socket.ConnectAsync(_endpoint, token).ConfigureAwait(false);
                Console.WriteLine("[Govee] Event stream connected.");
                await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException wse)
            {
                LogSocketError($"[Govee] Event stream socket error: {wse.Message}");
            }
            catch (Exception ex)
            {
                LogSocketError($"[Govee] Event stream error: {ex.Message}");
            }

            if (token.IsCancellationRequested)
                break;

            LogReconnect($"[Govee] Event stream reconnecting in {_reconnectDelay.TotalSeconds:F0}s...");
            try { await Task.Delay(_reconnectDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        Console.WriteLine("[Govee] Event stream stopped.");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var payloadStream = new MemoryStream();

        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var segment = new ArraySegment<byte>(buffer);
                WebSocketReceiveResult result = await socket.ReceiveAsync(segment, token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", token).ConfigureAwait(false);
                    break;
                }

                if (result.Count > 0)
                    payloadStream.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage)
                    continue;

                if (payloadStream.Length == 0)
                    continue;

                payloadStream.Position = 0;
                using var reader = new StreamReader(payloadStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var raw = await reader.ReadToEndAsync().ConfigureAwait(false);
                payloadStream.SetLength(0);

                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                await HandleRawMessageAsync(socket, raw, token).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleRawMessageAsync(ClientWebSocket socket, string raw, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            if (TryHandleHeartbeat(socket, root, token))
                return;

            var evt = GoveeJsonParser.ParseDeviceEvent(root);
            if (evt is not null)
            {
                DeviceEventReceived?.Invoke(this, evt);
                return;
            }
        }
        catch (JsonException)
        {
            Console.WriteLine("[Govee] Ignoring malformed event payload.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Failed to process event message: {ex.Message}");
        }
    }

    private bool TryHandleHeartbeat(ClientWebSocket socket, JsonElement root, CancellationToken token)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("msg", out var msgElement) && !root.TryGetProperty("message", out msgElement))
            return false;

        if (msgElement.ValueKind != JsonValueKind.String)
            return false;

        var msg = msgElement.GetString();
        if (!string.Equals(msg, "ping", StringComparison.OrdinalIgnoreCase))
            return false;

        _ = SendHeartbeatAsync(socket, token);
        return true;
    }

    private static async Task SendHeartbeatAsync(ClientWebSocket socket, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open)
            return;

        try
        {
            var payload = Encoding.UTF8.GetBytes("{\"msg\":\"pong\"}");
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Failed to send heartbeat: {ex.Message}");
        }
    }

    private void LogSocketError(string message)
    {
        if (TryTakeLogSlot(ref _lastSocketErrorLogUtc, ref _socketErrorSuppressed, ErrorLogCooldown, out var suppressed))
        {
            if (suppressed > 0)
                Console.WriteLine($"{message} (suppressed {suppressed} repeats)");
            else
                Console.WriteLine(message);
        }
    }

    private void LogReconnect(string message)
    {
        if (TryTakeLogSlot(ref _lastReconnectLogUtc, ref _reconnectSuppressed, ReconnectLogCooldown, out var suppressed))
        {
            if (suppressed > 0)
                Console.WriteLine($"{message} (suppressed {suppressed} repeats)");
            else
                Console.WriteLine(message);
        }
    }

    private bool TryTakeLogSlot(ref DateTimeOffset lastLoggedUtc, ref int suppressedCount, TimeSpan cooldown, out int suppressed)
    {
        lock (_logGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastLoggedUtc >= cooldown)
            {
                suppressed = suppressedCount;
                suppressedCount = 0;
                lastLoggedUtc = now;
                return true;
            }

            suppressedCount++;
            suppressed = 0;
            return false;
        }
    }
}
