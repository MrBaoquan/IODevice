using System.Net.Http.Json;
using IOToolkit.WebBridge.Configuration;
using IOToolkit.WebBridge.Hosting;
using IOToolkit.WebBridge.Runtime;

var failures = new List<string>();
await TestRuntime();
await TestServer();
await TestOriginPolicy();
await TestOriginEnforcement();
if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}
Console.WriteLine("{\"ok\":true,\"suite\":\"IOToolkit.WebBridge.Tests\",\"cases\":4}");
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
