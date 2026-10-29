using System.Text.Json;
using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class StatusEndpointTests
{
    private static readonly Guid Id = Guid.Parse("0192f3d0-4c67-7a19-9b55-8e3f1a7c2d40");

    private readonly ManualTimeProvider _time = new();

    private DiscoveryInstance Instance() => new(Id, "3.1.4", _time.GetUtcNow());

    private Task<(int StatusCode, StatusResponse Body)> Build(
        Func<Task<bool>>? isDown = null, Func<Task<bool>>? isDegraded = null) =>
        StatusEndpoint.BuildAsync("battle", Instance(), new LoadCounter(_time), _time, isDown, isDegraded);

    [Fact]
    public async Task A_healthy_instance_is_up_with_200()
    {
        var instance = Instance();
        _time.Advance(TimeSpan.FromSeconds(61.9));

        var (code, body) = await StatusEndpoint.BuildAsync("battle", instance, new LoadCounter(_time), _time);

        Assert.Equal(200, code);
        Assert.Equal("battle", body.Service);
        Assert.Equal(Id, body.InstanceId);
        Assert.Equal("UP", body.Status);
        Assert.Equal("3.1.4", body.Version);
        Assert.Equal(61, body.UptimeSeconds);
        Assert.Equal("2026-10-24T10:01:01.900Z", body.Time);
    }

    [Fact]
    public async Task Degraded_is_still_200()
    {
        var (code, body) = await Build(isDegraded: () => Task.FromResult(true));

        Assert.Equal((200, "DEGRADED"), (code, body.Status));
    }

    [Fact]
    public async Task Down_is_503_and_wins_over_degraded()
    {
        var (code, body) = await Build(() => Task.FromResult(true), () => Task.FromResult(true));

        Assert.Equal((503, "DOWN"), (code, body.Status));
    }

    [Fact]
    public async Task A_check_that_throws_counts_as_holding()
    {
        var (code, body) = await Build(() => throw new InvalidOperationException("database gone"));

        Assert.Equal((503, "DOWN"), (code, body.Status));
    }

    [Fact]
    public async Task The_json_has_exactly_the_contract_fields()
    {
        var (_, body) = await Build();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var root = json.RootElement;

        Assert.Equal(
            ["service", "instance_id", "status", "version", "uptime_seconds", "load", "time"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["in_flight_requests", "requests_last_minute", "avg_response_ms_last_minute"],
            root.GetProperty("load").EnumerateObject().Select(p => p.Name));
        Assert.Equal("0192f3d0-4c67-7a19-9b55-8e3f1a7c2d40", root.GetProperty("instance_id").GetString());
    }
}
