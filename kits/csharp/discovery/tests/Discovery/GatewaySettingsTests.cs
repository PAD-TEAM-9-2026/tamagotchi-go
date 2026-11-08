using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class GatewaySettingsTests
{
    private static GatewaySettings Read(params (string Name, string? Value)[] values)
    {
        var env = values.ToDictionary(v => v.Name, v => v.Value);
        return GatewaySettings.FromEnvironment(name => env.GetValueOrDefault(name));
    }

    [Fact]
    public void Nothing_set_means_the_defaults_and_a_window_of_seventeen_and_a_half_seconds()
    {
        var settings = Read();

        Assert.Equal(new Uri("http://gateway:3000"), settings.Url);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.Timeout);
        Assert.Equal(new BreakerSettings(3, TimeSpan.FromSeconds(17.5), TimeSpan.FromSeconds(30)), settings.Breaker);
    }

    [Fact]
    public void Blank_values_mean_the_defaults()
    {
        var settings = Read(
            (GatewaySettings.UrlVariable, " "),
            (GatewaySettings.TimeoutVariable, ""),
            (GatewaySettings.ThresholdVariable, " "),
            (GatewaySettings.MultiplierVariable, ""),
            (GatewaySettings.ResetVariable, ""));

        Assert.Equal(Read(), settings);
    }

    [Fact]
    public void The_window_is_the_timeout_times_the_multiplier()
    {
        var settings = Read(
            (GatewaySettings.UrlVariable, "http://localhost:3000"),
            (GatewaySettings.TimeoutVariable, "3000"),
            (GatewaySettings.ThresholdVariable, "5"),
            (GatewaySettings.MultiplierVariable, "2"),
            (GatewaySettings.ResetVariable, "10"));

        Assert.Equal(new Uri("http://localhost:3000"), settings.Url);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.Timeout);
        Assert.Equal(new BreakerSettings(5, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10)), settings.Breaker);
    }

    [Theory]
    [InlineData(GatewaySettings.TimeoutVariable, "fast")]
    [InlineData(GatewaySettings.TimeoutVariable, "0")]
    [InlineData(GatewaySettings.MultiplierVariable, "-1")]
    [InlineData(GatewaySettings.ResetVariable, "NaN")]
    [InlineData(GatewaySettings.ThresholdVariable, "2.5")]
    [InlineData(GatewaySettings.UrlVariable, "gateway:3000")]
    public void An_invalid_value_stops_the_service(string name, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read((name, value)));
        Assert.Contains(name, error.Message);
    }
}
