using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class DiscoverySettingsTests
{
    private static DiscoverySettings? Read(params (string Name, string? Value)[] values)
    {
        var env = values.ToDictionary(v => v.Name, v => v.Value);
        return DiscoverySettings.FromEnvironment(name => env.GetValueOrDefault(name), () => "container-1");
    }

    [Fact]
    public void Nothing_set_means_enabled_with_the_defaults()
    {
        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(new Uri("http://service-discovery:3009"), settings.Url);
        Assert.Equal("container-1", settings.Host);
    }

    [Fact]
    public void Blank_values_mean_the_defaults()
    {
        var settings = Read(
            (DiscoverySettings.EnabledVariable, " "),
            (DiscoverySettings.UrlVariable, ""),
            (DiscoverySettings.HostVariable, "  "));

        Assert.NotNull(settings);
        Assert.Equal(new Uri(DiscoverySettings.DefaultUrl), settings.Url);
        Assert.Equal("container-1", settings.Host);
    }

    [Fact]
    public void False_disables_discovery()
    {
        Assert.Null(Read((DiscoverySettings.EnabledVariable, "false")));
    }

    [Fact]
    public void The_url_and_host_can_be_set()
    {
        var settings = Read(
            (DiscoverySettings.UrlVariable, "http://localhost:3009/"),
            (DiscoverySettings.HostVariable, "battle-2"));

        Assert.NotNull(settings);
        Assert.Equal(new Uri("http://localhost:3009/"), settings.Url);
        Assert.Equal("battle-2", settings.Host);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public void Enabled_must_be_true_or_false(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read((DiscoverySettings.EnabledVariable, value)));
        Assert.Contains(DiscoverySettings.EnabledVariable, error.Message);
    }

    [Theory]
    [InlineData("service-discovery:3009")]
    [InlineData("ftp://service-discovery")]
    [InlineData("/v1")]
    public void The_url_must_be_absolute_http(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read((DiscoverySettings.UrlVariable, value)));
        Assert.Contains(DiscoverySettings.UrlVariable, error.Message);
    }
}
