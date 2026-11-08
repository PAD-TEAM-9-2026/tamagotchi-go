using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class RegistrationServiceTests
{
    private static readonly Guid Id = Guid.Parse("0192f3d0-4c67-7a19-9b55-8e3f1a7c2d40");
    private static readonly string Self = $"http://service-discovery:3009/v1/instances/{Id}";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly ManualDelays _delays = new();
    private readonly RecordingLogger<RegistrationService> _logs = new();

    private RegistrationService Create(
        FakeHandler handler, TimeSpan? checkInterval = null, IHostApplicationLifetime? lifetime = null) =>
        new(
            new HttpClient(handler),
            new DiscoverySettings(new Uri("http://service-discovery:3009/"), "battle-1"),
            new RegistrationOptions
            {
                ServiceName = "battle",
                Port = 3003,
                CheckInterval = checkInterval ?? Interval,
            },
            new DiscoveryInstance(Id, "3.1.4", DateTimeOffset.UnixEpoch),
            _logs,
            lifetime,
            _delays.Delay);

    private static HttpStatusCode Refused() => throw new HttpRequestException("Connection refused");

    [Fact]
    public void The_retry_delay_doubles_from_one_second_up_to_thirty()
    {
        Assert.Equal(
            new[] { 1, 1, 2, 4, 8, 16, 30, 30, 30 }.Select(s => TimeSpan.FromSeconds(s)),
            new[] { 0, 1, 2, 3, 4, 5, 6, 10, 100 }.Select(RegistrationService.RetryDelay));
    }

    [Fact]
    public async Task It_registers_on_start_with_the_status_contract_details()
    {
        var handler = new FakeHandler(_ => HttpStatusCode.Created);
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(Interval, await _delays.NextAsync());
        var call = Assert.Single(handler.Calls);
        Assert.Equal(("POST", "http://service-discovery:3009/v1/instances"), (call.Method, call.Url));
        var body = call.Body!.Value;
        Assert.Equal(Id.ToString(), body.GetProperty("instance_id").GetString());
        Assert.Equal("battle", body.GetProperty("service_name").GetString());
        Assert.Equal("battle-1", body.GetProperty("host").GetString());
        Assert.Equal(3003, body.GetProperty("port").GetInt32());
        Assert.Equal("/status", body.GetProperty("status_path").GetString());
        Assert.Equal("3.1.4", body.GetProperty("version").GetString());
        Assert.True(service.Registered);

        var line = Assert.Single(_logs.Named("service registered"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal(Id.ToString(), line.Fields["instance_id"]);
        Assert.Equal("created", line.Fields["status"]);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task It_retries_with_backoff_until_service_discovery_answers()
    {
        var attempts = 0;
        var handler = new FakeHandler(call =>
        {
            if (call.Method != "POST")
            {
                return HttpStatusCode.OK;
            }

            attempts++;
            return attempts == 1 ? Refused() : attempts == 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created;
        });
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(1), await _delays.NextAsync());
        Assert.False(service.Registered);
        Assert.Equal(TimeSpan.FromSeconds(2), await _delays.ReleaseAsync());
        Assert.Equal(Interval, await _delays.ReleaseAsync());
        Assert.Equal(["POST", "POST", "POST"], handler.Methods);
        Assert.True(service.Registered);

        var failures = _logs.Named("registration failed").ToList();
        Assert.All(failures, line => Assert.Equal(LogLevel.Warning, line.Level));
        Assert.Equal("HttpRequestException", failures[0].Fields["cause"]);
        Assert.Equal(1000L, failures[0].Fields["retry_in_ms"]);
        Assert.Equal(503, failures[1].Fields["status"]);
        Assert.Equal(2000L, failures[1].Fields["retry_in_ms"]);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task It_checks_itself_on_every_interval_while_registered()
    {
        var handler = new FakeHandler(call => call.Method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK);
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await _delays.ReleaseAsync();
        await _delays.ReleaseAsync();
        await _delays.ReleaseAsync();

        Assert.Equal(["POST", "GET", "GET", "GET"], handler.Methods);
        Assert.Equal(Self, handler.Calls[1].Url);
        Assert.Equal(3, _logs.Named("registration check passed").Count());

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task It_checks_every_thirty_seconds_by_default()
    {
        var handler = new FakeHandler(call => call.Method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK);
        var service = Create(handler, checkInterval: RegistrationService.DefaultCheckInterval);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(30), await _delays.NextAsync());
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task It_registers_again_only_when_service_discovery_has_lost_it()
    {
        var lost = true;
        var posts = 0;
        var handler = new FakeHandler(call =>
        {
            if (call.Method == "POST")
            {
                posts++;
                return posts == 1 ? HttpStatusCode.Created : HttpStatusCode.OK;
            }

            if (lost)
            {
                lost = false;
                return HttpStatusCode.NotFound;
            }

            return HttpStatusCode.OK;
        });
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        Assert.Equal(Interval, await _delays.ReleaseAsync());
        await _delays.ReleaseAsync();

        Assert.Equal(["POST", "GET", "POST", "GET"], handler.Methods);
        Assert.True(service.Registered);
        Assert.Equal(
            [("service registered", LogLevel.Information, "created"), ("service re-registered", LogLevel.Debug, "refreshed")],
            _logs.Lines
                .Where(line => line.Message is "service registered" or "service re-registered")
                .Select(line => (line.Message, line.Level, (string)line.Fields["status"]!)));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task It_keeps_checking_after_a_failed_check()
    {
        var checks = 0;
        var handler = new FakeHandler(call =>
        {
            if (call.Method == "POST")
            {
                return HttpStatusCode.Created;
            }

            checks++;
            return checks == 1 ? HttpStatusCode.InternalServerError : checks == 2 ? Refused() : HttpStatusCode.OK;
        });
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await _delays.ReleaseAsync();
        await _delays.ReleaseAsync();
        await _delays.ReleaseAsync();

        Assert.Equal(["POST", "GET", "GET", "GET"], handler.Methods);
        Assert.True(service.Registered);
        var failed = _logs.Named("registration check failed").ToList();
        Assert.Equal(2, failed.Count);
        Assert.Equal(500, failed[0].Fields["status"]);
        Assert.Equal("HttpRequestException", failed[1].Fields["cause"]);

        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task It_deregisters_on_stop(HttpStatusCode answer)
    {
        var handler = new FakeHandler(call => call.Method == "DELETE" ? answer : HttpStatusCode.Created);
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(("DELETE", Self), (handler.Calls[^1].Method, handler.Calls[^1].Url));
        Assert.False(service.Registered);
        Assert.Equal(LogLevel.Information, Assert.Single(_logs.Named("service deregistered")).Level);
    }

    [Fact]
    public async Task A_refused_deregistration_is_logged()
    {
        var handler = new FakeHandler(call =>
            call.Method == "DELETE" ? HttpStatusCode.InternalServerError : HttpStatusCode.Created);
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await service.StopAsync(CancellationToken.None);

        var line = Assert.Single(_logs.Named("deregistration failed"));
        Assert.Equal((LogLevel.Warning, (object?)500), (line.Level, line.Fields["status"]));
    }

    [Fact]
    public async Task An_unreachable_service_discovery_on_stop_is_logged()
    {
        var handler = new FakeHandler(call => call.Method == "DELETE" ? Refused() : HttpStatusCode.Created);
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await service.StopAsync(CancellationToken.None);

        var line = Assert.Single(_logs.Named("deregistration failed"));
        Assert.Equal((LogLevel.Warning, (object?)"HttpRequestException"), (line.Level, line.Fields["cause"]));
    }

    [Fact]
    public async Task Stopped_before_it_registered_it_sends_nothing_more()
    {
        var handler = new FakeHandler(_ => Refused());
        var service = Create(handler);

        await service.StartAsync(CancellationToken.None);
        await _delays.NextAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(["POST"], handler.Methods);
        Assert.Empty(_logs.Named("deregistration failed"));
    }

    [Fact]
    public async Task It_waits_until_the_server_listens()
    {
        var handler = new FakeHandler(_ => HttpStatusCode.Created);
        var lifetime = new FakeLifetime();
        var service = Create(handler, lifetime: lifetime);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.Empty(handler.Calls);

        lifetime.Start();

        Assert.Equal(Interval, await _delays.NextAsync());
        Assert.Equal(["POST"], handler.Methods);
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => _started.Cancel();

        public void StopApplication()
        {
        }
    }
}
