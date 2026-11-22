namespace Tamagotchi.Cache;

/// <summary>One cache shard. <see cref="ValkeyCacheNode"/> is the real one; tests use a fake.</summary>
public interface ICacheNode
{
    string Name { get; }

    /// <summary>The stored value, or null when the key is not there.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores the value. A null <paramref name="ttl"/> means no expiry (immutable versioned config only).</summary>
    Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
