using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IOToolkit.WebBridge.Configuration;
using IOToolkit.WebBridge.Runtime;

namespace IOToolkit.WebBridge.Hosting;

public sealed class BridgeServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BridgeOptions _options;
    private readonly BridgeConfig _config;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string, Exception?>? _log;
    private readonly ConcurrentDictionary<Guid, BridgeClient> _clients = new();
    private WebApplication? _app;

    public BridgeServer(BridgeOptions options, BridgeConfig config, DeviceRuntime runtime, Action<string, Exception?>? log = null)
    {
        _options = options;
        _config = config;
        _runtime = runtime;
        _log = log;
    }

    public Uri? BaseAddress { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = _options.ProjectRoot
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (_options.Port == 0)
                options.Listen(System.Net.IPAddress.Loopback, 0);
            else
                options.ListenLocalhost(_options.Port);
        });

        var app = builder.Build();
        Configure(app);
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.EventRaised += OnRuntimeEvent;
        await app.StartAsync(cancellationToken);
        _app = app;

        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        BaseAddress = address is null ? null : new Uri(address);
        _log?.Invoke($"WebBridge listening at {BaseAddress?.AbsoluteUri ?? "(unknown)"}", null);
    }

    private void Configure(WebApplication app)
    {
        var origins = new OriginPolicy(_options.AllowedOrigins);
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            var allowed = origins.Resolve(origin);
            if (allowed is not null)
            {
                context.Response.Headers.AccessControlAllowOrigin = allowed;
                context.Response.Headers.AccessControlAllowMethods = "GET,POST,OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-MyriadBox-Token";
            }

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                // A rejected preflight must not look like a successful one.
                context.Response.StatusCode = allowed is null && !string.IsNullOrWhiteSpace(origin)
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status204NoContent;
                return;
            }

            // Simple requests still need this check: the browser will send them whether or not the
            // response is readable, and a write fires the actuator regardless of CORS.
            if (!origins.IsAllowed(origin))
            {
                _log?.Invoke($"Rejected bridge request from origin '{origin}'.", null);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "origin_not_allowed",
                    origin,
                    hint = "Pass --allow-origin <origin> or set IOTOOLKIT_BRIDGE_ALLOWED_ORIGINS."
                });
                return;
            }

            // A loopback origin is not enough on its own: any page served from 127.0.0.1 would pass.
            // The shell hands its page a per-launch token, so require it when one is configured.
            //
            // The SDK script is the exception: a <script src> tag cannot attach a header, and the SDK
            // is what reads the token from the runtime bootstrap. Serving it is not a device action,
            // and only loopback origins got this far, so the capability stays local.
            if (_options.Token.Length > 0 && !IsSdkScript(context.Request.Path) && !HasValidToken(context))
            {
                _log?.Invoke($"Rejected bridge request with a missing or wrong token ({context.Request.Path}).", null);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "bridge_token_invalid" });
                return;
            }

            await next();
        });

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        app.MapGet("/", () => Results.Text("IOToolkit.WebBridge"));
        app.MapGet("/health", () => Results.Json(new
        {
            ok = true,
            service = "IOToolkit.WebBridge",
            version = typeof(BridgeServer).Assembly.GetName().Version?.ToString(),
            mock = _options.Mock,
            device = _config.Device,
            snapshot = _runtime.Snapshot
        }));
        app.MapGet("/api/v1/capabilities", () => Results.Json(BuildCapabilities()));
        app.MapGet("/api/v1/snapshot", () => Results.Json(_runtime.Snapshot));
        app.MapPost("/api/v1/channels/{channelId}", async (string channelId, SetChannelRequest request, CancellationToken ct) =>
        {
            await _runtime.SetAsync(channelId, request.Value, ct);
            return Results.Json(new { accepted = true, channel = channelId, value = request.Value });
        });
        app.MapPost("/api/v1/actions/{actionId}", async (string actionId, CancellationToken ct) =>
        {
            await _runtime.RunActionAsync(actionId, ct);
            return Results.Json(new { accepted = true, action = actionId });
        });
        app.MapPost("/api/v1/safe-stop", async (CancellationToken ct) =>
        {
            await _runtime.SafeStopAsync(ct);
            return Results.Json(new { accepted = true, safeStop = true });
        });
        app.MapPost("/api/v1/mock/input/{channelId}", async (string channelId, MockInputRequest request, CancellationToken ct) =>
        {
            await _runtime.TriggerMockInputAsync(channelId, request.Event);
            return Results.Json(new { accepted = true, channel = channelId, @event = request.Event });
        });
        app.MapGet("/sdk/iotoolkit.js", () =>
        {
            var sdkPath = Path.Combine(AppContext.BaseDirectory, "Sdk", "iotoolkit.js");
            return File.Exists(sdkPath)
                ? Results.File(sdkPath, "text/javascript; charset=utf-8")
                : Results.NotFound(new { error = "sdk_not_found" });
        });
        app.Map("/ws", HandleWebSocketAsync);
    }

    private object BuildCapabilities() => new
    {
        schema = _config.Schema,
        bridge = "IOToolkit.WebBridge",
        version = typeof(BridgeServer).Assembly.GetName().Version?.ToString(),
        mock = _options.Mock,
        device = _config.Device,
        pollIntervalMs = _config.PollIntervalMs,
        channels = _config.Channels.Select(x => new
        {
            x.Id,
            x.Key,
            x.Type,
            x.Read,
            x.Write,
            x.Min,
            x.Max,
            x.Unit,
            x.Events
        }),
        actions = _config.Actions.Select(x => new { x.Id, x.Channel, x.Value, x.PulseMs })
    };

    private static bool IsSdkScript(PathString path) =>
        path.StartsWithSegments("/sdk", StringComparison.OrdinalIgnoreCase);

    private bool HasValidToken(HttpContext context)
    {
        var provided = context.Request.Headers["X-MyriadBox-Token"].ToString();
        if (provided.Length == 0)
        {
            // Browsers cannot set headers on a WebSocket handshake, so the token also travels in the
            // query string for that one route.
            provided = context.Request.Query["token"].ToString();
        }
        if (provided.Length == 0)
            return false;

        var expected = Encoding.UTF8.GetBytes(_options.Token);
        var actual = Encoding.UTF8.GetBytes(provided);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new BridgeClient(socket);
        var id = Guid.NewGuid();
        _clients[id] = client;
        _log?.Invoke($"WebSocket client connected: {id}", null);

        try
        {
            await client.SendAsync(new
            {
                type = "hello",
                snapshot = _runtime.Snapshot,
                capabilities = BuildCapabilities()
            }, CancellationToken.None);

            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextAsync(socket, buffer, context.RequestAborted);
                if (message is null)
                    break;
                await HandleClientMessageAsync(client, message, context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Invoke($"WebSocket client {id} failed.", ex);
        }
        finally
        {
            _clients.TryRemove(id, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); }
                catch { }
            }
            _log?.Invoke($"WebSocket client disconnected: {id}", null);
        }
    }

    private async Task HandleClientMessageAsync(BridgeClient client, string message, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeElement))
            return;

        var type = typeElement.GetString();
        var requestId = root.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() : null;
        try
        {
            switch (type)
            {
                case "set":
                {
                    var channel = root.GetProperty("channel").GetString()!;
                    var value = root.GetProperty("value").GetDouble();
                    await _runtime.SetAsync(channel, value, cancellationToken);
                    await client.SendAsync(new { type = "ack", requestId, ok = true }, cancellationToken);
                    break;
                }
                case "action":
                {
                    var actionId = root.GetProperty("action").GetString()!;
                    await _runtime.RunActionAsync(actionId, cancellationToken);
                    await client.SendAsync(new { type = "ack", requestId, ok = true }, cancellationToken);
                    break;
                }
                case "safe-stop":
                    await _runtime.SafeStopAsync(cancellationToken);
                    await client.SendAsync(new { type = "ack", requestId, ok = true }, cancellationToken);
                    break;
                case "ping":
                    await client.SendAsync(new { type = "pong", requestId }, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            await client.SendAsync(new { type = "ack", requestId, ok = false, error = ex.Message }, cancellationToken);
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            memory.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        if (result.MessageType != WebSocketMessageType.Text)
            return null;
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private void OnSnapshotChanged(SnapshotFrame snapshot)
    {
        _ = BroadcastAsync(new { type = "snapshot", snapshot });
    }

    private void OnRuntimeEvent(RuntimeEvent runtimeEvent)
    {
        _ = BroadcastAsync(new { type = "event", name = runtimeEvent.Name, payload = runtimeEvent.Payload, timestamp = runtimeEvent.Timestamp });
    }

    private async Task BroadcastAsync(object message)
    {
        foreach (var client in _clients.Values)
        {
            try { await client.SendAsync(message, CancellationToken.None); }
            catch (Exception ex) { _log?.Invoke("WebSocket broadcast failed.", ex); }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
            return;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.EventRaised -= OnRuntimeEvent;
        foreach (var client in _clients.Values)
            client.Abort();
        _clients.Clear();
        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
        BaseAddress = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public sealed record SetChannelRequest(double Value);
    public sealed record MockInputRequest(string Event);

    private sealed class BridgeClient
    {
        private readonly WebSocket _socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public BridgeClient(WebSocket socket) => _socket = socket;

        public async Task SendAsync(object message, CancellationToken cancellationToken)
        {
            var text = JsonSerializer.Serialize(message, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Abort()
        {
            try { _socket.Abort(); } catch { }
        }
    }
}
