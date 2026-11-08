using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tamagotchi.Discovery;

public sealed class RegistrationOptions
{
    public required string ServiceName { get; init; }

    /// <summary>The port the service listens on inside its container.</summary>
    public required int Port { get; init; }

    public TimeSpan CheckInterval { get; init; } = RegistrationService.DefaultCheckInterval;
}

/// <summary>
/// Registers this instance with Service Discovery once the server listens, checks every 30 seconds that it is still
/// registered and registers again only when the answer is 404 (registering an existing id resets its health), and
/// deregisters on shutdown. Service Discovery being unreachable never stops the service: it retries with backoff.
/// </summary>
public sealed class RegistrationService : BackgroundService
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly DiscoverySettings _settings;
    private readonly RegistrationOptions _options;
    private readonly DiscoveryInstance _instance;
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Uri _instances;
    private readonly Uri _self;
    private readonly (string Name, object? Value)[] _fields;
    private volatile bool _registered;
    private int _failures;
    private bool _lost;

    public RegistrationService(
        HttpClient http,
        DiscoverySettings settings,
        RegistrationOptions options,
        DiscoveryInstance instance,
        ILogger<RegistrationService> logger,
        IHostApplicationLifetime? lifetime = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _settings = settings;
        _options = options;
        _instance = instance;
        _logger = logger;
        _lifetime = lifetime;
        _delay = delay ?? Task.Delay;

        var root = settings.Url.AbsoluteUri.TrimEnd('/');
        _instances = new Uri($"{root}/v1/instances");
        _self = new Uri($"{root}/v1/instances/{instance.Id}");
        _fields = [("instance_id", instance.Id.ToString()), ("host", settings.Host), ("port", options.Port)];
    }

    public bool Registered => _registered;

    /// <summary>1 s after the first failure, then doubling, never more than 30 s.</summary>
    public static TimeSpan RetryDelay(int failures)
    {
        var exponent = Math.Clamp(failures - 1, 0, 10);
        var delay = TimeSpan.FromTicks(FirstRetry.Ticks << exponent);
        return delay < MaxRetry ? delay : MaxRetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitUntilListening(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = _registered ? await CheckAsync(stoppingToken) : await RegisterAsync(stoppingToken);
                if (wait is { } span)
                {
                    await _delay(span, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; StopAsync deregisters.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!_registered)
        {
            return;
        }

        _registered = false;
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            using var response = await _http.DeleteAsync(_self, timeout.Token);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                _logger.LogDiscovery(LogLevel.Information, "service deregistered", _fields);
                return;
            }

            _logger.LogDiscovery(LogLevel.Warning, "deregistration failed", With(("status", (int)response.StatusCode)));
        }
        catch (Exception ex)
        {
            _logger.LogDiscovery(LogLevel.Warning, "deregistration failed", With(("cause", CauseOf(ex))));
        }
    }

    /// <summary>Registers; returns how long to wait before the next step.</summary>
    private async Task<TimeSpan> RegisterAsync(CancellationToken stoppingToken)
    {
        var body = new
        {
            instance_id = _instance.Id,
            service_name = _options.ServiceName,
            host = _settings.Host,
            port = _options.Port,
            status_path = StatusEndpoint.Path,
            version = _instance.Version,
        };

        int? status = null;
        string? cause = null;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var response = await _http.PostAsJsonAsync(_instances, body, timeout.Token);
                status = (int)response.StatusCode;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                cause = CauseOf(ex);
            }
        }

        if (status is 200 or 201)
        {
            _registered = true;
            _failures = 0;
            var fields = With(("status", status == 201 ? "created" : "refreshed"));
            if (_lost)
            {
                _logger.LogDiscovery(LogLevel.Debug, "service re-registered", fields);
            }
            else
            {
                _logger.LogDiscovery(LogLevel.Information, "service registered", fields);
            }

            _lost = false;
            return _options.CheckInterval;
        }

        _failures++;
        var retry = RetryDelay(_failures);
        var detail = status is { } code ? ("status", (object?)code) : ("cause", cause);
        _logger.LogDiscovery(LogLevel.Warning, "registration failed",
            With(detail, ("retry_in_ms", (long)retry.TotalMilliseconds)));
        return retry;
    }

    /// <summary>Checks the registration; returns how long to wait, or null to register again at once.</summary>
    private async Task<TimeSpan?> CheckAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.GetAsync(_self, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _registered = false;
                _lost = true;
                return null;
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogDiscovery(LogLevel.Debug, "registration check passed", _fields);
            }
            else
            {
                _logger.LogDiscovery(LogLevel.Warning, "registration check failed",
                    With(("status", (int)response.StatusCode)));
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDiscovery(LogLevel.Warning, "registration check failed", With(("cause", CauseOf(ex))));
        }

        return _options.CheckInterval;
    }

    private async Task WaitUntilListening(CancellationToken stoppingToken)
    {
        if (_lifetime is null)
        {
            return;
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        await started.Task;
    }

    private (string Name, object? Value)[] With(params (string Name, object? Value)[] extra) => [.. _fields, .. extra];

    /// <summary>A timeout is named as one; anything else by its type, never its message (it may hold an address).</summary>
    private static string CauseOf(Exception ex) => ex is OperationCanceledException ? "timeout" : ex.GetType().Name;
}
