using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Tamagotchi.Discovery.Tests;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed record RecordedCall(string Method, string Url, JsonElement? Body);

/// <summary>An HTTP server stand-in: each call is recorded and answered by the test's function.</summary>
public sealed class FakeHandler(Func<RecordedCall, HttpStatusCode> answer) : HttpMessageHandler
{
    public List<RecordedCall> Calls { get; } = [];

    public IEnumerable<string> Methods => Calls.Select(call => call.Method);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        JsonElement? body = null;
        if (request.Content is not null)
        {
            body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
        }

        var call = new RecordedCall(request.Method.Method, request.RequestUri!.ToString(), body);
        lock (Calls)
        {
            Calls.Add(call);
        }

        return new HttpResponseMessage(answer(call));
    }
}

public sealed record LogLine(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Fields);

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogLine> _lines = [];

    public IReadOnlyList<LogLine> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToList();
            }
        }
    }

    public IEnumerable<LogLine> Named(string message) => Lines.Where(line => line.Message == message);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
        lock (_lines)
        {
            _lines.Add(new LogLine(logLevel, formatter(state, exception), fields.ToDictionary(f => f.Key, f => f.Value)));
        }
    }
}

/// <summary>
/// Stands in for Task.Delay: every wait the service asks for is handed to the test, which reads how long it was and
/// lets it end with <see cref="ReleaseAsync"/>. Nothing waits on the real clock.
/// </summary>
public sealed class ManualDelays
{
    private readonly Channel<(TimeSpan Span, TaskCompletionSource Done)> _waits = Channel.CreateUnbounded<(TimeSpan, TaskCompletionSource)>();

    public Task Delay(TimeSpan span, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        _waits.Writer.TryWrite((span, done));
        return done.Task;
    }

    /// <summary>The next wait the service asked for, without ending it.</summary>
    public async Task<TimeSpan> NextAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (span, done) = await _waits.Reader.ReadAsync(limit.Token);
        _current = done;
        return span;
    }

    /// <summary>Ends the wait read last, and returns the one after it.</summary>
    public async Task<TimeSpan> ReleaseAsync()
    {
        _current?.TrySetResult();
        return await NextAsync();
    }

    private TaskCompletionSource? _current;
}
