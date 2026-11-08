using System.Globalization;
using System.Text.Json.Serialization;

namespace Tamagotchi.Discovery;

/// <summary>The body of <c>GET /status</c>, the <c>Status</c> shape of the main repository.</summary>
public sealed record StatusResponse(
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("instance_id")] Guid InstanceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("uptime_seconds")] long UptimeSeconds,
    [property: JsonPropertyName("load")] LoadSnapshot Load,
    [property: JsonPropertyName("time")] string Time);

public static class StatusEndpoint
{
    public const string Path = "/status";

    /// <summary>
    /// The answer to <c>/status</c>: 503 and <c>DOWN</c> when <paramref name="isDown"/> holds, otherwise 200 with
    /// <c>DEGRADED</c> when <paramref name="isDegraded"/> holds, or <c>UP</c>. A check that throws counts as holding.
    /// </summary>
    public static async Task<(int StatusCode, StatusResponse Body)> BuildAsync(
        string serviceName,
        DiscoveryInstance instance,
        LoadCounter load,
        TimeProvider time,
        Func<Task<bool>>? isDown = null,
        Func<Task<bool>>? isDegraded = null)
    {
        var down = await Holds(isDown);
        var degraded = !down && await Holds(isDegraded);
        var now = time.GetUtcNow();
        var body = new StatusResponse(
            serviceName,
            instance.Id,
            down ? "DOWN" : degraded ? "DEGRADED" : "UP",
            instance.Version,
            Math.Max(0, (long)Math.Floor((now - instance.StartedAt).TotalSeconds)),
            load.Snapshot(),
            now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        return (down ? 503 : 200, body);
    }

    private static async Task<bool> Holds(Func<Task<bool>>? check)
    {
        if (check is null)
        {
            return false;
        }

        try
        {
            return await check();
        }
        catch (Exception)
        {
            return true;
        }
    }
}
