using StackExchange.Redis;

namespace Tamagotchi.Cache;

/// <summary>
/// A Valkey shard through StackExchange.Redis. The connection is opened in the background and retried by the client, so
/// a shard that is down at startup only causes misses until it is reachable.
/// </summary>
public sealed class ValkeyCacheNode : ICacheNode, IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection;

    public ValkeyCacheNode(CacheNode node, TimeSpan operationBudget)
    {
        ArgumentNullException.ThrowIfNull(node);
        Name = node.Name;
        var options = ConfigurationOptions.Parse(node.Endpoint);
        options.AbortOnConnectFail = false;
        options.ClientName = $"tamagotchi-cache-{node.Name}";
        options.ConnectTimeout = 1000;
        options.SyncTimeout = (int)Math.Max(1, operationBudget.TotalMilliseconds);
        options.AsyncTimeout = (int)Math.Max(1, operationBudget.TotalMilliseconds);
        _connection = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(options));
    }

    public string Name { get; }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var db = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
        var value = await db.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public async Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        var db = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
        var expiry = ttl is { } t ? new Expiration(t) : Expiration.Default;
        await db.StringSetAsync(key, value, expiry).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var db = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
        await db.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray()).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            var connection = await _connection.Value.ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IDatabase> DatabaseAsync(CancellationToken cancellationToken)
    {
        var connection = await _connection.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return connection.GetDatabase();
    }
}
