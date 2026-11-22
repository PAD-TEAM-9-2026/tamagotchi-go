using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tamagotchi.Cache;

public sealed class CacheSettings
{
    /// <summary>The longest TTL a key may have. Only immutable versioned config may have none.</summary>
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(1);

    /// <summary>The service label of <c>cache_requests_total</c>, for example <c>user-management</c>.</summary>
    public required string Service { get; init; }

    /// <summary>How long one cache operation may take before the node counts as failed.</summary>
    public TimeSpan OperationBudget { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a failed node is skipped.</summary>
    public TimeSpan DownFor { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>The <c>result</c> label of <c>cache_requests_total</c>.</summary>
public enum CacheResult
{
    Hit,
    Miss,
    Error,
}

/// <summary>Receives every cache result, so the service can count it in its own <c>cache_requests_total</c>.</summary>
public interface ICacheObserver
{
    void Record(string service, string node, CacheResult result);
}

/// <summary>A value read through the cache, and whether the cache answered. <see cref="Header"/> is the X-Cache value.</summary>
public sealed record CacheLookup<T>(T? Value, bool Hit)
{
    public const string HeaderName = "X-Cache";

    public string Header => Hit ? "HIT" : "MISS";
}

/// <summary>The stored form of every value: <c>{body, etag, cached_at}</c>.</summary>
public sealed record CacheEntry(
    [property: JsonPropertyName("body")] JsonElement Body,
    [property: JsonPropertyName("etag")] string Etag,
    [property: JsonPropertyName("cached_at")] DateTimeOffset CachedAt);

/// <summary>
/// Cache-aside over the shards of the ring. The cache is optional: a node that is down, slow or failing is skipped and
/// the value is loaded from the database, so a cache problem never becomes an error for the caller. Invalidate after the
/// database commit, on the write path.
/// </summary>
public sealed class ShardedCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HashRing? _ring;
    private readonly IReadOnlyDictionary<string, ICacheNode> _nodes;
    private readonly CacheSettings _settings;
    private readonly TimeProvider _time;
    private readonly NodeHealth _health;
    private readonly ICacheObserver? _observer;
    private readonly ILogger _logger;

    public ShardedCache(
        IEnumerable<ICacheNode> nodes,
        CacheSettings settings,
        TimeProvider time,
        ICacheObserver? observer = null,
        ILogger<ShardedCache>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        _nodes = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);
        _ring = _nodes.Count == 0 ? null : new HashRing(_nodes.Keys);
        _settings = settings;
        _time = time;
        _health = new NodeHealth(time, settings.DownFor);
        _observer = observer;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>False when <c>CACHE_NODES</c> is empty: every read is a miss that goes to the database.</summary>
    public bool Enabled => _ring is not null;

    public string? NodeFor(string key) => _ring?.NodeFor(key);

    /// <summary>
    /// Reads <paramref name="key"/>; on a miss, calls <paramref name="load"/> and stores a non-null result with
    /// <paramref name="ttl"/>. A null result (not found) is not stored.
    /// </summary>
    public async Task<CacheLookup<T>> GetOrLoadAsync<T>(
        string key,
        TimeSpan? ttl,
        Func<CancellationToken, Task<T?>> load,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(load);
        ValidateTtl(ttl);

        var node = Node(key);
        if (node is not null)
        {
            var stored = await TryAsync(node, "read", ct => node.GetAsync(key, ct), cancellationToken).ConfigureAwait(false);
            if (stored.Ok && stored.Value is not null && TryRead<T>(stored.Value, out var cached))
            {
                Record(node, CacheResult.Hit);
                return new CacheLookup<T>(cached, true);
            }

            Record(node, stored.Ok ? CacheResult.Miss : CacheResult.Error);
        }

        var value = await load(cancellationToken).ConfigureAwait(false);
        if (node is not null && value is not null && !_health.IsDown(node.Name))
        {
            var entry = Serialize(value);
            await TryAsync(node, "write", async ct =>
            {
                await node.SetAsync(key, entry, ttl, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        return new CacheLookup<T>(value, false);
    }

    /// <summary>Deletes the keys from their nodes. Call it after the database commit. Failures are logged, never thrown.</summary>
    public async Task InvalidateAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (_ring is null)
        {
            return;
        }

        foreach (var group in keys.Distinct(StringComparer.Ordinal).GroupBy(_ring.NodeFor, StringComparer.Ordinal))
        {
            var node = _nodes[group.Key];
            var batch = group.ToArray();
            await TryAsync(node, "delete", async ct =>
            {
                await node.DeleteAsync(batch, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task InvalidateAsync(string key, CancellationToken cancellationToken) =>
        InvalidateAsync([key], cancellationToken);

    private ICacheNode? Node(string key)
    {
        if (_ring is null)
        {
            return null;
        }

        var node = _nodes[_ring.NodeFor(key)];
        if (_health.IsDown(node.Name))
        {
            Record(node, CacheResult.Error);
            return null;
        }

        return node;
    }

    private async Task<(bool Ok, TResult? Value)> TryAsync<TResult>(
        ICacheNode node,
        string action,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_settings.OperationBudget);
        try
        {
            return (true, await operation(budget.Token).WaitAsync(_settings.OperationBudget, _time, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.MarkDown(node.Name);
            _logger.LogWarning(
                "Cache node {CacheNode} failed a {CacheAction} ({ErrorType}); skipped for {DownForSeconds} s",
                node.Name, action, ex.GetType().Name, _settings.DownFor.TotalSeconds);
            return (false, default);
        }
    }

    private string Serialize<T>(T value)
    {
        var body = JsonSerializer.SerializeToElement(value, Json);
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())))[..32]}\"";
        return JsonSerializer.Serialize(new CacheEntry(body, etag, _time.GetUtcNow()), Json);
    }

    private static bool TryRead<T>(string stored, out T? value)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(stored, Json);
            value = entry is null ? default : entry.Body.Deserialize<T>(Json);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private void Record(ICacheNode node, CacheResult result) => _observer?.Record(_settings.Service, node.Name, result);

    private static void ValidateTtl(TimeSpan? ttl)
    {
        if (ttl is { } t && (t <= TimeSpan.Zero || t > CacheSettings.MaxTtl))
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "A cache TTL must be above zero and at most one hour.");
        }
    }
}
