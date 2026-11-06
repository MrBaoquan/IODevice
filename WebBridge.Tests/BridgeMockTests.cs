using System.Net.Http.Json;
using IOToolkit.WebBridge.Configuration;
using IOToolkit.WebBridge.Hosting;
using IOToolkit.WebBridge.Runtime;

namespace IOToolkit.WebBridge.Tests;

public sealed class BridgeMockTests
{
    [Fact]
    public async Task RuntimeWritesPublishesAndTriggersMockInput()
    {
        var options = new BridgeOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Mock = true,
            Port = 0
        };
        var config = CreateConfig();
        await using var runtime = new DeviceRuntime(options, config);
        await runtime.StartAsync();

        await runtime.SetAsync("lamp.start", 1);
        Assert.Equal(1, runtime.Snapshot.Values["lamp.start"]);

        RuntimeEvent? inputEvent = null;
        runtime.EventRaised += evt => inputEvent = evt;
        await runtime.TriggerMockInputAsync("button.start", "pressed");

        Assert.Equal(1, runtime.Snapshot.Values["button.start"]);
        Assert.NotNull(inputEvent);
        Assert.Equal("input.button.start.pressed", inputEvent!.Name);
    }

    [Fact]
    public async Task BridgeServerExposesHttpContract()
    {
        var options = new BridgeOptions { ProjectRoot = Path.GetTempPath(), Mock = true, Port = 0 };
        var config = CreateConfig();
        await using var runtime = new DeviceRuntime(options, config);
        await using var server = new BridgeServer(options, config, runtime);
        await runtime.StartAsync();
        await server.StartAsync();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        var health = await client.GetFromJsonAsync<HealthResponse>("/health");
        Assert.NotNull(health);
        Assert.True(health!.Ok);
        Assert.True(health.Mock);

        var response = await client.PostAsJsonAsync("/api/v1/channels/lamp.start", new { value = 1 });
        response.EnsureSuccessStatusCode();
        var snapshot = await client.GetFromJsonAsync<SnapshotFrame>("/api/v1/snapshot");
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.Values["lamp.start"]);

        var mockResponse = await client.PostAsJsonAsync("/api/v1/mock/input/button.start", new { @event = "pressed" });
        mockResponse.EnsureSuccessStatusCode();
        snapshot = await client.GetFromJsonAsync<SnapshotFrame>("/api/v1/snapshot");
        Assert.Equal(1, snapshot!.Values["button.start"]);
    }

    private static BridgeConfig CreateConfig() => new()
    {
        Device = "MockDevice",
        PollIntervalMs = 10,
        Channels =
        [
            new BridgeChannel { Id = "button.start", Key = "Button_00", Type = "DI" },
            new BridgeChannel { Id = "lamp.start", Key = "Lamp", Type = "OACTION", Write = true, Min = 0, Max = 1, SafeValue = 0 }
        ],
        Actions =
        [
            new BridgeAction { Id = "lamp.pulse", Channel = "lamp.start", Value = 1, PulseMs = 20 }
        ]
    };

    private sealed record HealthResponse(bool Ok, string Service, string Version, bool Mock, string Device);
}
