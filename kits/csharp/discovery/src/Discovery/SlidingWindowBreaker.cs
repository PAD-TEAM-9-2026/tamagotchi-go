namespace Tamagotchi.Discovery;

public sealed record BreakerSettings(int Threshold, TimeSpan Window, TimeSpan Reset);

/// <summary>
/// Sliding-window circuit breaker: closed, open, half-open. <see cref="BreakerSettings.Threshold"/> failures inside
/// <see cref="BreakerSettings.Window"/> open it, and <paramref name="onTrip"/> is told. While open every call is refused.
/// After <see cref="BreakerSettings.Reset"/> one trial call goes through: a success closes it (and
/// <paramref name="onClose"/> is told), a failure opens it again.
/// </summary>
public sealed class SlidingWindowBreaker(
    BreakerSettings settings,
    TimeProvider time,
    Action<int, string> onTrip,
    Action onClose)
{
    private enum State
    {
        Closed,
        Open,
        HalfOpen,
    }

    private readonly object _lock = new();
    private readonly Queue<long> _failures = new();
    private State _state = State.Closed;
    private long _openedAt;
    private bool _trialRunning;

    public BreakerSettings Settings => settings;

    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _state == State.Closed;
            }
        }
    }

    /// <summary>Whether a call may go out now. In half-open only one trial call is let through at a time.</summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_state == State.Open && time.GetElapsedTime(_openedAt) >= settings.Reset)
            {
                _state = State.HalfOpen;
                _trialRunning = false;
            }

            switch (_state)
            {
                case State.Closed:
                    return true;
                case State.HalfOpen when !_trialRunning:
                    _trialRunning = true;
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        var closed = false;
        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                _state = State.Closed;
                _trialRunning = false;
                _failures.Clear();
                closed = true;
            }
        }

        if (closed)
        {
            onClose();
        }
    }

    public void RecordFailure(string reason)
    {
        int? tripped = null;
        lock (_lock)
        {
            var now = time.GetTimestamp();
            while (_failures.Count > 0 && time.GetElapsedTime(_failures.Peek(), now) >= settings.Window)
            {
                _failures.Dequeue();
            }

            _failures.Enqueue(now);

            if (_state == State.HalfOpen)
            {
                _state = State.Open;
                _openedAt = now;
                _trialRunning = false;
            }
            else if (_state == State.Closed && _failures.Count >= settings.Threshold)
            {
                _state = State.Open;
                _openedAt = now;
                tripped = _failures.Count;
            }
        }

        if (tripped is { } count)
        {
            onTrip(count, reason);
        }
    }

    /// <summary>A call that ended without saying anything about the target (the caller cancelled it).</summary>
    public void RecordAbandoned()
    {
        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                _trialRunning = false;
            }
        }
    }
}
