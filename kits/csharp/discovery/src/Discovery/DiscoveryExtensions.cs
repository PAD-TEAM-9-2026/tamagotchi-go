using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tamagotchi.Discovery;

public static class DiscoveryExtensions
{
    /// <summary>
    /// Adds the instance, the load counter and, when <paramref name="settings"/> is not null, the registration with
    /// Service Discovery. Pass <c>DiscoverySettings.FromEnvironment()</c>; it is null when discovery is disabled.
    /// </summary>
    public static IServiceCollection AddDiscovery(
        this IServiceCollection services, string serviceName, int port, DiscoverySettings? settings)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => DiscoveryInstance.Create(provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<LoadCounter>();

        if (settings is null)
        {
            return services;
        }

        services.AddHostedService(provider => new RegistrationService(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }),
            settings,
            new RegistrationOptions { ServiceName = serviceName, Port = port },
            provider.GetRequiredService<DiscoveryInstance>(),
            provider.GetRequiredService<ILogger<RegistrationService>>(),
            provider.GetRequiredService<IHostApplicationLifetime>()));
        return services;
    }

    /// <summary>Counts requests for <c>/status</c>. Put it first, so it sees every request and its full duration.</summary>
    public static IApplicationBuilder UseLoadCounter(this IApplicationBuilder app) =>
        app.UseMiddleware<LoadCounterMiddleware>();

    /// <summary>
    /// Maps <c>GET /status</c>. <paramref name="isDown"/> makes it 503 <c>DOWN</c> (for example when the database is
    /// unreachable), <paramref name="isDegraded"/> makes it <c>DEGRADED</c>.
    /// </summary>
    public static RouteHandlerBuilder MapStatusEndpoint(
        this IEndpointRouteBuilder app,
        string serviceName,
        Func<HttpContext, Task<bool>>? isDown = null,
        Func<HttpContext, Task<bool>>? isDegraded = null) =>
        app.MapGet(StatusEndpoint.Path, async (HttpContext http, DiscoveryInstance instance, LoadCounter load, TimeProvider time) =>
        {
            var (statusCode, body) = await StatusEndpoint.BuildAsync(
                serviceName,
                instance,
                load,
                time,
                isDown is null ? null : () => isDown(http),
                isDegraded is null ? null : () => isDegraded(http));
            return Results.Json(body, statusCode: statusCode);
        });
}
