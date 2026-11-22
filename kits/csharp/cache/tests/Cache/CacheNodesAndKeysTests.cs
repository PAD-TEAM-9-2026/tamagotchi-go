using Xunit;

namespace Tamagotchi.Cache.Tests;

public sealed class CacheNodesAndKeysTests
{
    [Fact]
    public void Cache_nodes_are_read_in_order_with_names_and_endpoints()
    {
        var nodes = CacheNodes.Parse("cache-0=cache-0:6379, cache-1=cache-1:6379,cache-2=10.0.0.5:6380");

        Assert.Equal(
            [new CacheNode("cache-0", "cache-0:6379"), new CacheNode("cache-1", "cache-1:6379"), new CacheNode("cache-2", "10.0.0.5:6380")],
            nodes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_means_no_cache(string? value) => Assert.Empty(CacheNodes.Parse(value));

    [Theory]
    [InlineData("cache-0")]
    [InlineData("=cache-0:6379")]
    [InlineData("cache-0=")]
    [InlineData("cache-0=a:1,cache-0=b:2")]
    public void Malformed_values_are_refused(string value) =>
        Assert.Throws<FormatException>(() => CacheNodes.Parse(value));

    [Fact]
    public void A_key_without_a_query_has_four_parts() =>
        Assert.Equal("user-management:v1:profile:42", CacheKeys.For("user-management", "profile", "42"));

    [Fact]
    public void The_same_query_in_any_order_gives_the_same_key()
    {
        var a = CacheKeys.For("battle", "battles", "u1", [new("status", "ACTIVE"), new("page", "2")]);
        var b = CacheKeys.For("battle", "battles", "u1", [new("page", "2"), new("status", "ACTIVE"), new("empty", "")]);

        Assert.Equal(a, b);
        Assert.Matches("^battle:v1:battles:u1:[0-9a-f]{16}$", a);
    }

    [Fact]
    public void The_query_hash_is_the_first_sixteen_hex_characters_of_sha256() =>
        Assert.Equal("7f2466a37222e0e4", CacheKeys.QueryHash("page=2&status=ACTIVE"));

    [Fact]
    public void An_empty_query_adds_no_hash() =>
        Assert.Equal("battle:v1:battle:7", CacheKeys.For("battle", "battle", "7", [new("x", null)]));

    [Theory]
    [InlineData("", "profile", "1")]
    [InlineData("svc:x", "profile", "1")]
    [InlineData("svc", "pro:file", "1")]
    [InlineData("svc", "profile", " ")]
    public void Bad_key_parts_are_refused(string service, string resource, string id) =>
        Assert.Throws<ArgumentException>(() => CacheKeys.For(service, resource, id));
}
