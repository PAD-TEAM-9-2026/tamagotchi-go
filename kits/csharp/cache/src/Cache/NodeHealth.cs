using System.Collections.Concurrent;

namespace Tamagotchi.Cache;

/// <summary>
/// A node that timed out or failed is skipped for <see cref="CacheSettings.DownFor"/>. Its keys are not moved to another
/// node: reads go to the database until it is back. Only a change of <c>CACHE_NODES</c> changes the ring.
/// </summary>
public sealed class NodeHealth(TimeProvider time, TimeSpan downFor)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _downUntil = new(StringComparer.Ordinal);

    public bool IsDown(string node) =>
        _downUntil.TryGetValue(node, out var until) && time.GetUtcNow() < until;

    public void MarkDown(string node) => _downUntil[node] = time.GetUtcNow() + downFor;
}
