using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Tamagotchi.Discovery;

/// <summary>The <c>load</c> object of <c>/status</c>.</summary>
public sealed record LoadSnapshot(
    [property: JsonPropertyName("in_flight_requests")] int InFlightRequests,
    [property: JsonPropertyName("requests_last_minute")] int RequestsLastMinute,
    [property: JsonPropertyName("avg_response_ms_last_minute")] double AvgResponseMsLastMinute);

/// <summary>Requests being handled now, and the requests that finished in the last 60 seconds with their durations.</summary>
public sealed class LoadCounter(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly Queue<(long At, double DurationMs)> _finished = new();
    private int _inFlight;

    public void Started()
    {
        lock (_lock)
        {
            _inFlight++;
        }
    }

    public void Finished(TimeSpan duration)
    {
        lock (_lock)
        {
            _inFlight = Math.Max(0, _inFlight - 1);
            _finished.Enqueue((time.GetTimestamp(), duration.TotalMilliseconds));
            Prune();
        }
    }

    public LoadSnapshot Snapshot()
    {
        lock (_lock)
        {
            Prune();
            var count = _finished.Count;
            var total = _finished.Sum(entry => entry.DurationMs);
            var average = count == 0 ? 0 : Math.Round(total / count, 1, MidpointRounding.AwayFromZero);
            return new LoadSnapshot(_inFlight, count, average);
        }
    }

    private void Prune()
    {
        var now = time.GetTimestamp();
        while (_finished.Count > 0 && time.GetElapsedTime(_finished.Peek().At, now) >= Window)
        {
            _finished.Dequeue();
        }
    }
}

/// <summary>Counts every request in <see cref="LoadCounter"/>, except the three probes.</summary>
public sealed class LoadCounterMiddleware(RequestDelegate next, LoadCounter load, TimeProvider time)
{
    public static readonly string[] ProbePaths = ["/health", "/ready", "/status"];

    public static bool IsProbe(PathString path) =>
        ProbePaths.Any(probe => path.Equals(probe, StringComparison.OrdinalIgnoreCase));

    public async Task InvokeAsync(HttpContext http)
    {
        if (IsProbe(http.Request.Path))
        {
            await next(http);
            return;
        }

        var started = time.GetTimestamp();
        load.Started();
        try
        {
            await next(http);
        }
        finally
        {
            load.Finished(time.GetElapsedTime(started));
        }
    }
}
