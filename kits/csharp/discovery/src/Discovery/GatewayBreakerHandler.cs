using Microsoft.Extensions.Logging;

namespace Tamagotchi.Discovery;

/// <summary>
/// Thrown at once, without a network call, while the Gateway breaker is open. It is an
/// <see cref="HttpRequestException"/>, so code that already treats an unreachable dependency as a 502 keeps doing so.
/// </summary>
public sealed class BreakerOpenException : HttpRequestException
{
    public BreakerOpenException()
        : base("The circuit breaker for the Gateway is open.")
    {
    }
}

/// <summary>
/// Puts every call through the shared Gateway breaker and bounds it with <c>GATEWAY_TIMEOUT_MS</c>.
/// Only a timeout or a connection error counts as a failure: a 5xx from the Gateway usually comes from the service
/// behind it, a 4xx is never a failure, and a call the caller cancelled says nothing about the Gateway.
/// </summary>
public sealed class GatewayBreakerHandler(SlidingWindowBreaker breaker, TimeSpan timeout) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!breaker.TryEnter())
        {
            throw new BreakerOpenException();
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var response = await base.SendAsync(request, deadline.Token);
            breaker.RecordSuccess();
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            breaker.RecordFailure("timeout");
            throw new TaskCanceledException(
                $"The Gateway did not answer within {timeout.TotalMilliseconds} ms.",
                new TimeoutException());
        }
        catch (HttpRequestException)
        {
            breaker.RecordFailure("connection_error");
            throw;
        }
        catch
        {
            breaker.RecordAbandoned();
            throw;
        }
    }

    /// <summary>The Gateway breaker, logging <c>circuit breaker tripped</c> (error) and <c>circuit breaker closed</c> (info).</summary>
    public static SlidingWindowBreaker CreateBreaker(GatewaySettings settings, TimeProvider time, ILogger logger)
    {
        var target = settings.Url.AbsoluteUri.TrimEnd('/');
        return new SlidingWindowBreaker(
            settings.Breaker,
            time,
            (failures, reason) => logger.LogDiscovery(
                LogLevel.Error,
                "circuit breaker tripped",
                ("target", target),
                ("failures_in_window", failures),
                ("threshold", settings.Breaker.Threshold),
                ("window_seconds", settings.Breaker.Window.TotalSeconds),
                ("last_reason", reason)),
            () => logger.LogDiscovery(LogLevel.Information, "circuit breaker closed", ("target", target)));
    }
}
