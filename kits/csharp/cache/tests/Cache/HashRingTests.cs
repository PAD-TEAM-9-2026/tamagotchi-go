using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Tamagotchi.Cache.Tests;

public sealed class HashRingTests
{
    private static readonly string[] ThreeNodes = ["cache-0", "cache-1", "cache-2"];

    [Theory]
    [InlineData("cache-0#0", 146176822303492358UL)]
    [InlineData("cache-1#127", 3413361906762385281UL)]
    [InlineData("", 15284527576400310788UL)]
    [InlineData("ü", 13849713055468560845UL)]
    public void The_hash_is_the_first_eight_md5_bytes_big_endian(string text, ulong expected) =>
        Assert.Equal(expected, HashRing.Hash(text));

    [Theory]
    [InlineData("user-management:v1:profile:0000", "cache-1")]
    [InlineData("battle:v1:battle:3", "cache-0")]
    [InlineData("registry:v1:package:pkg-11", "cache-1")]
    [InlineData("", "cache-1")]
    [InlineData("a", "cache-2")]
    [InlineData("ü-ключ", "cache-1")]
    [InlineData("tamagotchi:v1:creature:x:0123456789abcdef", "cache-0")]
    public void Keys_land_on_the_reference_node_with_three_nodes(string key, string node) =>
        Assert.Equal(node, new HashRing(ThreeNodes).NodeFor(key));

    [Theory]
    [InlineData("user-management:v1:profile:0000", "cache-0")]
    [InlineData("registry:v1:package:pkg-11", "cache-2")]
    [InlineData("", "cache-0")]
    [InlineData("a", "cache-2")]
    public void Keys_land_on_the_reference_node_with_cache_1_removed(string key, string node) =>
        Assert.Equal(node, new HashRing(["cache-0", "cache-2"]).NodeFor(key));

    [Fact]
    public void The_order_of_the_node_list_does_not_matter()
    {
        var a = new HashRing(ThreeNodes);
        var b = new HashRing(["cache-2", "cache-0", "cache-1"]);

        foreach (var key in Keys(2000))
        {
            Assert.Equal(a.NodeFor(key), b.NodeFor(key));
        }
    }

    [Fact]
    public void Removing_a_node_moves_only_the_keys_it_owned()
    {
        var before = new HashRing(ThreeNodes);
        var after = new HashRing(["cache-0", "cache-2"]);

        foreach (var key in Keys(5000))
        {
            var was = before.NodeFor(key);
            Assert.Equal(was == "cache-1" ? after.NodeFor(key) : was, after.NodeFor(key));
            Assert.NotEqual("cache-1", after.NodeFor(key));
        }
    }

    [Fact]
    public void Adding_a_node_moves_only_the_keys_it_takes_over_about_a_quarter()
    {
        var before = new HashRing(ThreeNodes);
        var after = new HashRing([.. ThreeNodes, "cache-3"]);
        var keys = Keys(20000).ToArray();

        var moved = keys.Where(k => before.NodeFor(k) != after.NodeFor(k)).ToArray();

        Assert.All(moved, k => Assert.Equal("cache-3", after.NodeFor(k)));
        Assert.InRange(moved.Length / (double)keys.Length, 0.15, 0.35);
    }

    [Fact]
    public void One_hundred_twenty_eight_virtual_nodes_spread_keys_evenly_enough()
    {
        var ring = new HashRing(ThreeNodes);
        var counts = Keys(30000).GroupBy(ring.NodeFor).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, c => Assert.InRange(c / 30000.0, 0.20, 0.47));
    }

    [Fact]
    public void A_single_node_owns_every_key()
    {
        var ring = new HashRing(["cache-0"]);

        Assert.All(Keys(500), k => Assert.Equal("cache-0", ring.NodeFor(k)));
    }

    [Fact]
    public void Nodes_are_listed_in_ordinal_order() =>
        Assert.Equal(ThreeNodes, new HashRing(["cache-2", "cache-0", "cache-1"]).Nodes);

    [Fact]
    public void Empty_and_duplicate_node_lists_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new HashRing([]));
        Assert.Throws<ArgumentException>(() => new HashRing(["cache-0", "cache-0"]));
        Assert.Throws<ArgumentException>(() => new HashRing(["cache-0", " "]));
    }

    /// <summary>
    /// The shared <c>contracts/cache-ring-vectors.json</c>: every key's <c>hash</c> and every case's <c>expected</c>
    /// node. The file is the one <c>CACHE_RING_VECTORS</c> names, else <c>cache-ring-vectors.json</c> copied into the
    /// test output; without either the test has nothing to check.
    /// </summary>
    [Fact]
    public void The_shared_ring_vectors_pass_when_the_file_is_present()
    {
        var path = Environment.GetEnvironmentVariable("CACHE_RING_VECTORS");
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "cache-ring-vectors.json");
            if (!File.Exists(path))
            {
                return;
            }
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(HashRing.VirtualNodes, root.GetProperty("virtual_nodes").GetInt32());

        var keys = root.GetProperty("keys").EnumerateArray().ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Equal(key.GetProperty("hash").GetString(), HashRing.Hash(key.GetProperty("key").GetString()!).ToString(CultureInfo.InvariantCulture));
        }

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.NotEmpty(cases);
        foreach (var testCase in cases)
        {
            var ring = new HashRing(testCase.GetProperty("nodes").EnumerateArray().Select(n => n.GetString()!));
            foreach (var expected in testCase.GetProperty("expected").EnumerateObject())
            {
                Assert.Equal(expected.Value.GetString(), ring.NodeFor(expected.Name));
            }
        }
    }

    private static IEnumerable<string> Keys(int count) =>
        Enumerable.Range(0, count).Select(i => $"svc:v1:resource:{i}");
}
