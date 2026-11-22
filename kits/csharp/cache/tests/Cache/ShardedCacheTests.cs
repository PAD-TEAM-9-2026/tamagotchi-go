using System.Collections.Concurrent;
using Xunit;

namespace Tamagotchi.Cache.Tests;

public sealed class ShardedCacheTests
{
    private sealed record Profile(string Id, string Name);

    [Fact]
    public async Task A_miss_loads_stores_and_the_next_read_is_a_hit()
    {
        var (cache, nodes, observer, _) = Build();
        var loads = 0;

        var first = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(5), _ => Load(ref loads), default);
        var second = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(5), _ => Load(ref loads), default);

        Assert.False(first.Hit);
        Assert.Equal("MISS", first.Header);
        Assert.True(second.Hit);
        Assert.Equal("HIT", second.Header);
        Assert.Equal(new Profile("1", "Ana"), second.Value);
        Assert.Equal(1, loads);
        var owner = cache.NodeFor("svc:v1:profile:1")!;
        Assert.Equal(TimeSpan.FromMinutes(5), nodes[owner].Ttls["svc:v1:profile:1"]);
        Assert.Equal([("svc", owner, CacheResult.Miss), ("svc", owner, CacheResult.Hit)], observer.Results);
    }

    [Fact]
    public async Task The_stored_value_is_body_etag_and_cached_at()
    {
        var (cache, nodes, _, time) = Build();
        var loads = 0;

        await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        var stored = nodes[cache.NodeFor("svc:v1:profile:1")!].Values["svc:v1:profile:1"];
        Assert.Contains("\"body\":{\"id\":\"1\",\"name\":\"Ana\"}", stored, StringComparison.Ordinal);
        Assert.Matches("\"etag\":\"\\\\u0022[0-9a-f]{32}\\\\u0022\"", stored);
        Assert.Contains($"\"cached_at\":\"{time.GetUtcNow():yyyy-MM-ddTHH:mm:ss}", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_key_is_stored_on_the_node_the_ring_chooses()
    {
        var (cache, nodes, _, _) = Build();
        var loads = 0;
        var keys = Enumerable.Range(0, 60).Select(i => $"svc:v1:profile:{i}").ToArray();

        foreach (var key in keys)
        {
            await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        }

        foreach (var key in keys)
        {
            var owner = cache.NodeFor(key)!;
            Assert.All(nodes, n => Assert.Equal(n.Key == owner, n.Value.Values.ContainsKey(key)));
        }

        Assert.All(nodes.Values, n => Assert.NotEmpty(n.Values));
    }

    [Fact]
    public async Task A_not_found_result_is_not_stored()
    {
        var (cache, nodes, _, _) = Build();

        var result = await cache.GetOrLoadAsync<Profile>("svc:v1:profile:404", TimeSpan.FromMinutes(1), _ => Task.FromResult<Profile?>(null), default);

        Assert.Null(result.Value);
        Assert.False(result.Hit);
        Assert.All(nodes.Values, n => Assert.Empty(n.Values));
    }

    [Fact]
    public async Task Invalidation_deletes_the_key_from_its_node()
    {
        var (cache, nodes, _, _) = Build();
        var loads = 0;
        await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        await cache.InvalidateAsync("svc:v1:profile:1", default);
        var after = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.False(after.Hit);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task A_failing_node_is_skipped_for_ten_seconds_and_reads_still_succeed()
    {
        var (cache, nodes, observer, time) = Build();
        var loads = 0;
        var key = "svc:v1:profile:1";
        var owner = nodes[cache.NodeFor(key)!];
        owner.Fail = true;

        var first = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        var callsAfterFailure = owner.Calls;
        var second = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.Equal(new Profile("1", "Ana"), first.Value);
        Assert.Equal(new Profile("1", "Ana"), second.Value);
        Assert.Equal(callsAfterFailure, owner.Calls);
        Assert.Equal(2, loads);
        Assert.All(observer.Results, r => Assert.Equal(CacheResult.Error, r.Result));

        owner.Fail = false;
        time.Advance(TimeSpan.FromSeconds(10));
        await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        var back = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.True(back.Hit);
    }

    [Fact]
    public async Task A_down_node_does_not_move_its_keys_to_another_node()
    {
        var (cache, nodes, _, _) = Build();
        var loads = 0;
        var key = "svc:v1:profile:1";
        var owner = nodes[cache.NodeFor(key)!];
        owner.Fail = true;

        await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.All(nodes.Values, n => Assert.False(n.Values.ContainsKey(key)));
    }

    [Fact]
    public async Task A_slow_node_counts_as_failed_after_the_budget()
    {
        var (cache, nodes, observer, _) = Build(TimeProvider.System);
        var loads = 0;
        var key = "svc:v1:profile:1";
        nodes[cache.NodeFor(key)!].Delay = TimeSpan.FromSeconds(5);

        var started = DateTime.UtcNow;
        var result = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.Equal(new Profile("1", "Ana"), result.Value);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.Equal(CacheResult.Error, observer.Results.Single().Result);
    }

    [Fact]
    public async Task A_failed_invalidation_is_not_thrown()
    {
        var (cache, nodes, _, _) = Build();
        foreach (var node in nodes.Values)
        {
            node.Fail = true;
        }

        await cache.InvalidateAsync(["svc:v1:profile:1", "svc:v1:profile:2"], default);
    }

    [Fact]
    public async Task Without_nodes_the_cache_is_disabled_and_every_read_loads()
    {
        var cache = new ShardedCache([], new CacheSettings { Service = "svc" }, new TestClock());
        var loads = 0;

        var a = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        var b = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        await cache.InvalidateAsync("svc:v1:profile:1", default);

        Assert.False(cache.Enabled);
        Assert.False(a.Hit || b.Hit);
        Assert.Equal(2, loads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public async Task Ttls_must_be_positive_and_at_most_one_hour(int seconds)
    {
        var (cache, _, _, _) = Build();
        var loads = 0;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cache.GetOrLoadAsync("svc:v1:x:1", TimeSpan.FromSeconds(seconds), _ => Load(ref loads), default));
    }

    [Fact]
    public async Task Immutable_config_may_be_stored_without_a_ttl()
    {
        var (cache, nodes, _, _) = Build();
        var loads = 0;

        await cache.GetOrLoadAsync("registry:v1:stats:7", null, _ => Load(ref loads), default);

        Assert.Null(nodes[cache.NodeFor("registry:v1:stats:7")!].Ttls["registry:v1:stats:7"]);
    }

    [Fact]
    public async Task A_corrupt_stored_value_is_a_miss_and_is_replaced()
    {
        var (cache, nodes, _, _) = Build();
        var loads = 0;
        nodes[cache.NodeFor("svc:v1:profile:1")!].Values["svc:v1:profile:1"] = "not json";

        var result = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);
        var again = await cache.GetOrLoadAsync("svc:v1:profile:1", TimeSpan.FromMinutes(1), _ => Load(ref loads), default);

        Assert.False(result.Hit);
        Assert.True(again.Hit);
    }

    [Fact]
    public async Task The_callers_cancellation_is_passed_through()
    {
        var (cache, _, _, _) = Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetOrLoadAsync<Profile>("svc:v1:profile:1", TimeSpan.FromMinutes(1), ct => Task.FromCanceled<Profile?>(ct), cts.Token));
    }

    private static Task<Profile?> Load(ref int loads)
    {
        loads++;
        return Task.FromResult<Profile?>(new Profile("1", "Ana"));
    }

    private static (ShardedCache Cache, Dictionary<string, FakeNode> Nodes, RecordingObserver Observer, TestClock Time) Build(TimeProvider? real = null)
    {
        var clock = new TestClock();
        var nodes = new[] { "cache-0", "cache-1", "cache-2" }.ToDictionary(n => n, n => new FakeNode(n));
        var observer = new RecordingObserver();
        var cache = new ShardedCache(nodes.Values, new CacheSettings { Service = "svc" }, real ?? clock, observer);
        return (cache, nodes, observer, clock);
    }

    private sealed class FakeNode(string name) : ICacheNode
    {
        public ConcurrentDictionary<string, string> Values { get; } = new();

        public ConcurrentDictionary<string, TimeSpan?> Ttls { get; } = new();

        public bool Fail { get; set; }

        public TimeSpan Delay { get; set; }

        public int Calls { get; private set; }

        public string Name { get; } = name;

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            await Before(cancellationToken);
            return Values.TryGetValue(key, out var value) ? value : null;
        }

        public async Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
        {
            await Before(cancellationToken);
            Values[key] = value;
            Ttls[key] = ttl;
        }

        public async Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            await Before(cancellationToken);
            foreach (var key in keys)
            {
                Values.TryRemove(key, out _);
            }
        }

        private async Task Before(CancellationToken cancellationToken)
        {
            Calls++;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Fail)
            {
                throw new InvalidOperationException("node down");
            }
        }
    }

    private sealed class RecordingObserver : ICacheObserver
    {
        public List<(string Service, string Node, CacheResult Result)> Results { get; } = [];

        public void Record(string service, string node, CacheResult result) => Results.Add((service, node, result));
    }
}

/// <summary>A clock that moves only when told. Timers are not used by the code under test.</summary>
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
