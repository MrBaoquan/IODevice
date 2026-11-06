using System.Net.Http.Json;
using IOToolkit.WebBridge.Configuration;
using IOToolkit.WebBridge.Hosting;
using IOToolkit.WebBridge.Runtime;

var failures = new List<string>();
await TestRuntime();
await TestServer();
await TestOriginPolicy();
await TestOriginEnforcement();
await TestSafeValueRange();
await TestBridgeToken();
if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}
Console.WriteLine("{\"ok\":true,\"suite\":\"IOToolkit.WebBridge.Tests\",\"cases\":6}");
return 0;

async Task TestRuntime()
{
    try
    {
        var options = new BridgeOptions { ProjectRoot = Path.GetTempPath(), Mock = true, Port = 0 };
        var config = CreateConfig();
        await using var runtime = new DeviceRuntime(options, config);
        await runtime.StartAsync();
        await runtime.SetAsync("lamp.start", 1);
        Assert(runtime.Snapshot.Values["lamp.start"] == 1, "runtime write did not update snapshot");
        RuntimeEvent? inputEvent = null;
        runtime.EventRaised += evt => inputEvent = evt;
        await runtime.TriggerMockInputAsync("button.start", "pressed");
        Assert(runtime.Snapshot.Values["button.start"] == 1, "mock input did not update snapshot");
        Assert(inputEvent?.Name == "input.button.start.pressed", "mock input event missing");
    }
    catch (Exception ex) { failures.Add("runtime: " + ex); }
}

async Task TestServer()
{
    try
    {
        var options = new BridgeOptions { ProjectRoot = Path.GetTempPath(), Mock = true, Port = 0 };
        var config = CreateConfig();
        await using var runtime = new DeviceRuntime(options, config);
        await using var server = new BridgeServer(options, config, runtime);
        await runtime.StartAsync();
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        var health = await client.GetFromJsonAsync<HealthResponse>("/health");
        Assert(health?.Ok == true && health.Mock, "health endpoint is not ready");
        var response = await client.PostAsJsonAsync("/api/v1/channels/lamp.start", new { value = 1 });
        response.EnsureSuccessStatusCode();
        var snapshot = await client.GetFromJsonAsync<SnapshotFrame>("/api/v1/snapshot");
        Assert(snapshot?.Values["lamp.start"] == 1, "HTTP write did not update snapshot");
        var mock = await client.PostAsJsonAsync("/api/v1/mock/input/button.start", new { @event = "pressed" });
        mock.EnsureSuccessStatusCode();
        snapshot = await client.GetFromJsonAsync<SnapshotFrame>("/api/v1/snapshot");
        Assert(snapshot?.Values["button.start"] == 1, "HTTP mock input did not update snapshot");
    }
    catch (Exception ex) { failures.Add("server: " + ex); }
}

async Task TestOriginPolicy()
{
    try
    {        var policy = new OriginPolicy(["http://192.168.1.50:3000", "https://Control.Example/"]);

        // Non-browser callers (the MyriadBox shell, curl, native hosts) send no Origin.
        Assert(policy.IsAllowed(null), "requests without Origin must be allowed");
        Assert(policy.Resolve(null) is null, "requests without Origin must not get CORS headers");

        // The exhibit shell serves the page from a loopback port it picked at startup.
        Assert(policy.IsAllowed("http://127.0.0.1:19200"), "loopback origin must be allowed");
        Assert(policy.IsAllowed("http://localhost:19100"), "localhost origin must be allowed");
        Assert(policy.IsAllowed("http://[::1]:19100"), "IPv6 loopback origin must be allowed");
        Assert(policy.Resolve("http://127.0.0.1:19200") == "http://127.0.0.1:19200",
            "loopback origin must be echoed verbatim");

        // Explicit whitelist, including the trailing-slash normalisation.
        Assert(policy.IsAllowed("http://192.168.1.50:3000"), "whitelisted origin must be allowed");
        Assert(policy.IsAllowed("https://control.example"), "whitelist match must be case-insensitive");
        Assert(policy.IsAllowed("https://control.example/"), "whitelist match must ignore a trailing slash");

        // Anything else must be refused, including lookalikes.
        Assert(!policy.IsAllowed("https://evil.example"), "unknown origin must be refused");
        Assert(!policy.IsAllowed("http://127.0.0.1.evil.example"), "lookalike loopback host must be refused");
        Assert(!policy.IsAllowed("http://192.168.1.51:3000"), "neighbour host must be refused");
        Assert(!policy.IsAllowed("null"), "opaque origin must be refused");
        Assert(!policy.IsAllowed("file://"), "file origin must be refused");
        Assert(policy.Resolve("https://evil.example") is null, "refused origin must not be echoed");

        // Default configuration stays usable for the shell without any flags.
        var defaults = new OriginPolicy(null);
        Assert(defaults.IsAllowed("http://127.0.0.1:1234"), "default policy must allow loopback");
        Assert(!defaults.IsAllowed("https://evil.example"), "default policy must refuse remote origins");
    }
    catch (Exception ex) { failures.Add("origin-policy: " + ex); }
    await Task.CompletedTask;
}

async Task TestOriginEnforcement()
{
    try
    {
        var options = new BridgeOptions { ProjectRoot = Path.GetTempPath(), Mock = true, Port = 0 };
        var config = CreateConfig();
        var runtime = new DeviceRuntime(options, config);
        var server = new BridgeServer(options, config, runtime);
        try
        {
            runtime.StartAsync().GetAwaiter().GetResult();
            server.StartAsync().GetAwaiter().GetResult();
            using var client = new HttpClient { BaseAddress = server.BaseAddress };

            // A foreign origin must not be able to drive an actuator, even though the write would
            // otherwise succeed.
            using var blocked = new HttpRequestMessage(HttpMethod.Post, "/api/v1/channels/lamp.start")
            {
                Content = JsonContent.Create(new { value = 1 })
            };
            blocked.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
            var blockedResponse = client.Send(blocked);
            Assert((int)blockedResponse.StatusCode == 403, "foreign origin write must be refused with 403");
            Assert(runtime.Snapshot.Values.TryGetValue("lamp.start", out var parked) && Math.Abs(parked) < 0.000001,
                "a refused write must not reach the actuator");

            // A rejected preflight must not report success.
            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/channels/lamp.start");
            preflight.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
            preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            var preflightResponse = client.Send(preflight);
            Assert((int)preflightResponse.StatusCode == 403, "foreign preflight must be refused with 403");
            Assert(!preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out _),
                "refused preflight must not advertise an allowed origin");

            // The shell (no Origin header) keeps working.
            var shellWrite = client.PostAsJsonAsync("/api/v1/channels/lamp.start", new { value = 1 })
                .GetAwaiter().GetResult();
            Assert(shellWrite.IsSuccessStatusCode, "shell request without Origin must still be accepted");
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
    catch (Exception ex) { failures.Add("origin-enforcement: " + ex); }
    await Task.CompletedTask;
}

async Task TestSafeValueRange()
{
    try
    {
        // A safe value outside the channel's travel range must be rejected at load time: writing it
        // would drive the mechanism past its limit at the moment it is supposed to stop.
        var below = AssertThrows(() => Channel("axis", min: -30, max: 30, safeValue: -45));
        Assert(below is not null, "safeValue below min must be rejected");
        Assert(below!.Contains("below min", StringComparison.OrdinalIgnoreCase),
            "rejection must name the bound it violated");

        var above = AssertThrows(() => Channel("axis", min: -30, max: 30, safeValue: 45));
        Assert(above is not null, "safeValue above max must be rejected");
        Assert(above!.Contains("above max", StringComparison.OrdinalIgnoreCase),
            "rejection must name the bound it violated");

        // In-range values, and channels without bounds, stay valid.
        Assert(AssertThrows(() => Channel("axis", min: -30, max: 30, safeValue: 0)) is null,
            "in-range safeValue must be accepted");
        Assert(AssertThrows(() => Channel("axis", min: null, max: null, safeValue: 9999)) is null,
            "unbounded channel must accept any safeValue");

        // Runtime backstop: even if a value slipped past validation it is clamped, never sent as-is.
        var channel = Channel("axis", min: -30, max: 30, safeValue: 0);
        channel.SafeValue = -45;
        Assert(Math.Abs(channel.ClampSafeValue() - (-30)) < 0.000001, "runtime safe value must clamp to min");
        channel.SafeValue = 45;
        Assert(Math.Abs(channel.ClampSafeValue() - 30) < 0.000001, "runtime safe value must clamp to max");
    }
    catch (Exception ex) { failures.Add("safe-value-range: " + ex); }
    await Task.CompletedTask;
}

string? AssertThrows(Action action)
{
    try { action(); return null; }
    catch (InvalidDataException ex) { return ex.Message; }
}

BridgeChannel Channel(string id, double? min, double? max, double safeValue)
{
    var channel = new BridgeChannel
    {
        Id = id,
        Key = "Pitch",
        Type = "OAXIS",
        Write = true,
        Min = min,
        Max = max,
        SafeValue = safeValue,
    };
    channel.Validate();
    return channel;
}

async Task TestBridgeToken()
{
    try
    {
        const string token = "test-token-1234";
        var options = new BridgeOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Mock = true,
            Port = 0,
            Token = token,
        };
        var config = CreateConfig();
        var runtime = new DeviceRuntime(options, config);
        var server = new BridgeServer(options, config, runtime);
        try
        {
            runtime.StartAsync().GetAwaiter().GetResult();
            server.StartAsync().GetAwaiter().GetResult();
            using var client = new HttpClient { BaseAddress = server.BaseAddress };

            // No token: a loopback page without the shared secret must not move an actuator.
            var unauthenticated = await client.PostAsJsonAsync("/api/v1/channels/lamp.start", new { value = 1 });
            Assert((int)unauthenticated.StatusCode == 403, "request without the bridge token must be refused");
            Assert(runtime.Snapshot.Values.TryGetValue("lamp.start", out var parked) && Math.Abs(parked) < 0.000001,
                "a refused write must not reach the actuator");

            // Wrong token must be refused too.
            using (var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/v1/channels/lamp.start"))
            {
                wrong.Content = JsonContent.Create(new { value = 1 });
                wrong.Headers.TryAddWithoutValidation("X-MyriadBox-Token", "not-the-token");
                var wrongResponse = await client.SendAsync(wrong);
                Assert((int)wrongResponse.StatusCode == 403, "a wrong bridge token must be refused");
            }

            // Correct token in the header.
            using (var correct = new HttpRequestMessage(HttpMethod.Post, "/api/v1/channels/lamp.start"))
            {
                correct.Content = JsonContent.Create(new { value = 1 });
                correct.Headers.TryAddWithoutValidation("X-MyriadBox-Token", token);
                var correctResponse = await client.SendAsync(correct);
                Assert(correctResponse.IsSuccessStatusCode, "the shared token must be accepted");
            }
            Assert(runtime.Snapshot.Values["lamp.start"] == 1, "an authenticated write must reach the actuator");

            // WebSocket handshakes cannot set headers, so the query-string form must work.
            using (var queryToken = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/snapshot?token={token}"))
            {
                var snapshot = await client.SendAsync(queryToken);
                Assert(snapshot.IsSuccessStatusCode, "the token must also be accepted as a query parameter");
            }
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // With no token configured the bridge stays usable for local debugging.
        var openOptions = new BridgeOptions { ProjectRoot = Path.GetTempPath(), Mock = true, Port = 0 };
        var openRuntime = new DeviceRuntime(openOptions, CreateConfig());
        var openServer = new BridgeServer(openOptions, CreateConfig(), openRuntime);
        try
        {
            openRuntime.StartAsync().GetAwaiter().GetResult();
            openServer.StartAsync().GetAwaiter().GetResult();
            using var client = new HttpClient { BaseAddress = openServer.BaseAddress };
            var response = await client.PostAsJsonAsync("/api/v1/channels/lamp.start", new { value = 1 });
            Assert(response.IsSuccessStatusCode, "a bridge with no configured token must accept requests");
        }
        finally
        {
            openServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            openRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
    catch (Exception ex) { failures.Add("bridge-token: " + ex); }
    await Task.CompletedTask;
}

void Assert(bool condition, string message) { if (!condition) failures.Add(message); }
static BridgeConfig CreateConfig() => new()
{
    Device = "MockDevice",
    PollIntervalMs = 10,
    Channels =
    [
        new BridgeChannel { Id = "button.start", Key = "Button_00", Type = "DI" },
        new BridgeChannel { Id = "lamp.start", Key = "Lamp", Type = "OACTION", Write = true, Min = 0, Max = 1, SafeValue = 0 }
    ],
    Actions = [new BridgeAction { Id = "lamp.pulse", Channel = "lamp.start", Value = 1, PulseMs = 20 }]
};

internal sealed record HealthResponse(bool Ok, string Service, string Version, bool Mock, string Device);
