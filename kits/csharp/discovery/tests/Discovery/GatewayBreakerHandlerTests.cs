using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class GatewayBreakerHandlerTests
{
    private static readonly GatewaySettings Settings = new(
        new Uri("http://gateway:3000/"),
        TimeSpan.FromMilliseconds(50),
        new BreakerSettings(3, TimeSpan.FromSeconds(17.5), TimeSpan.FromSeconds(30)));

    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<GatewayBreakerHandlerTests> _logs = new();
    private readonly SlidingWindowBreaker _breaker;

    public GatewayBreakerHandlerTests()
    {
        _breaker = GatewayBreakerHandler.CreateBreaker(Settings, _time, _logs);
    }

    private HttpClient Client(StubHandler gateway) =>
        new(new GatewayBreakerHandler(_breaker, Settings.Timeout) { InnerHandler = gateway })
        {
            BaseAddress = Settings.Url,
            Timeout = Timeout.InfiniteTimeSpan,
        };

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_answer_counts_as_a_success_even_a_5xx(HttpStatusCode status)
    {
        var client = Client(StubHandler.Answering(status));

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/registry/v1/packages");
            Assert.Equal(status, response.StatusCode);
        }

        Assert.True(_breaker.IsClosed);
    }

    [Fact]
    public async Task Three_connection_errors_trip_it_and_log_the_trip()
    {
        var client = Client(StubHandler.Refusing());

        for (var i = 0; i < 3; i++)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/v1/x"));
            Assert.IsNotType<BreakerOpenException>(error);
        }

        var line = Assert.Single(_logs.Named("circuit breaker tripped"));
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("http://gateway:3000", line.Fields["target"]);
        Assert.Equal(3, line.Fields["failures_in_window"]);
        Assert.Equal(3, line.Fields["threshold"]);
        Assert.Equal(17.5, line.Fields["window_seconds"]);
        Assert.Equal("connection_error", line.Fields["last_reason"]);
    }

    [Fact]
    public async Task Timeouts_count_and_end_as_a_timeout()
    {
        var client = Client(StubHandler.Hanging());

        for (var i = 0; i < 3; i++)
        {
            var error = await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("/v1/x"));
            Assert.IsType<TimeoutException>(error.InnerException);
        }

        Assert.False(_breaker.IsClosed);
        Assert.Equal("timeout", Assert.Single(_logs.Named("circuit breaker tripped")).Fields["last_reason"]);
    }

    [Fact]
    public async Task While_open_calls_fail_at_once_without_reaching_the_gateway()
    {
        var gateway = StubHandler.Refusing();
        var client = Client(gateway);
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/v1/x"));
        }

        await Assert.ThrowsAsync<BreakerOpenException>(() => client.GetAsync("/v1/x"));

        Assert.Equal(3, gateway.Calls);
    }

    [Fact]
    public async Task A_call_the_caller_cancelled_does_not_count()
    {
        var client = Client(StubHandler.Hanging());

        for (var i = 0; i < 5; i++)
        {
            using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/v1/x", caller.Token));
            Assert.IsNotType<TimeoutException>(error.InnerException);
        }

        Assert.True(_breaker.IsClosed);
    }

    [Fact]
    public async Task A_successful_trial_after_the_reset_closes_it_and_logs_it()
    {
        var failing = true;
        var gateway = new StubHandler(_ => failing
            ? throw new HttpRequestException("Connection refused")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = Client(gateway);
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/v1/x"));
        }

        failing = false;
        _time.Advance(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync("/v1/x");

        Assert.True(_breaker.IsClosed);
        var line = Assert.Single(_logs.Named("circuit breaker closed"));
        Assert.Equal((LogLevel.Information, (object?)"http://gateway:3000"), (line.Level, line.Fields["target"]));
    }
}
