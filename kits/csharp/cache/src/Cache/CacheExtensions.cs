using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tamagotchi.Cache;

public static class CacheExtensions
{
    /// <summary>
    /// Registers one <see cref="ShardedCache"/> built from <c>CACHE_NODES</c>. With the variable empty or missing the cache
    /// is disabled and every read goes to the database. Register an <see cref="ICacheObserver"/> to count the results.
    /// </summary>
    public static IServiceCollection AddShardedCache(this IServiceCollection services, IConfiguration configuration, string service)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var settings = new CacheSettings { Service = service };
        var nodes = CacheNodes.Parse(configuration[CacheNodes.VariableName]);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddSingleton(sp => new ShardedCache(
            nodes.Select(n => (ICacheNode)new ValkeyCacheNode(n, settings.OperationBudget)).ToArray(),
            settings,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ICacheObserver>(),
            sp.GetService<ILogger<ShardedCache>>()));
        return services;
    }
}
