using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class SlidingWindowBreakerTests
{
    private static readonly BreakerSettings Settings =
        new(Threshold: 3, Window: TimeSpan.FromSeconds(17.5), Reset: TimeSpan.FromSeconds(30));

    private readonly ManualTimeProvider _time = new();
    private readonly List<(int Failures, string Reason)> _trips = [];
    private int _closes;

    private SlidingWindowBreaker Create() =>
        new(Settings, _time, (failures, reason) => _trips.Add((failures, reason)), () => _closes++);

    private static void Fail(SlidingWindowBreaker breaker, int times, string reason = "timeout")
    {
        for (var i = 0; i < times; i++)
        {
            breaker.RecordFailure(reason);
        }
    }

    [Fact]
    public void A_new_breaker_lets_calls_through()
    {
        var breaker = Create();

        Assert.True(breaker.TryEnter());
        Assert.True(breaker.IsClosed);
    }

    [Fact]
    public void Two_failures_do_not_trip_it()
    {
        var breaker = Create();

        Fail(breaker, 2);

        Assert.True(breaker.TryEnter());
        Assert.Empty(_trips);
    }

    [Fact]
    public void Three_failures_inside_the_window_trip_it_once_and_refuse_calls()
    {
        var breaker = Create();

        Fail(breaker, 2);
        breaker.RecordFailure("connection_error");

        Assert.False(breaker.TryEnter());
        Assert.Equal([(3, "connection_error")], _trips);
    }

    [Fact]
    public void Failures_older_than_the_window_do_not_count()
    {
        var breaker = Create();
        Fail(breaker, 2);

        _time.Advance(TimeSpan.FromSeconds(17.5));
        breaker.RecordFailure("timeout");

        Assert.True(breaker.TryEnter());
        Assert.Empty(_trips);
    }

    [Fact]
    public void After_the_reset_one_trial_call_goes_through_at_a_time()
    {
        var breaker = Create();
        Fail(breaker, 3);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(breaker.TryEnter());

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(breaker.TryEnter());
        Assert.False(breaker.TryEnter());
    }

    [Fact]
    public void A_successful_trial_closes_it_and_starts_counting_afresh()
    {
        var breaker = Create();
        Fail(breaker, 3);
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(breaker.TryEnter());

        breaker.RecordSuccess();

        Assert.True(breaker.IsClosed);
        Assert.Equal(1, _closes);
        Fail(breaker, 2);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public void A_failed_trial_opens_it_again_for_a_full_reset_without_a_second_trip_line()
    {
        var breaker = Create();
        Fail(breaker, 3);
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(breaker.TryEnter());

        breaker.RecordFailure("timeout");

        Assert.False(breaker.TryEnter());
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(breaker.TryEnter());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(breaker.TryEnter());
        Assert.Single(_trips);
    }

    [Fact]
    public void An_abandoned_trial_lets_the_next_call_try()
    {
        var breaker = Create();
        Fail(breaker, 3);
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(breaker.TryEnter());

        breaker.RecordAbandoned();

        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public void A_success_while_closed_changes_nothing()
    {
        var breaker = Create();
        Fail(breaker, 2);

        breaker.RecordSuccess();
        breaker.RecordFailure("timeout");

        Assert.False(breaker.TryEnter());
        Assert.Equal(0, _closes);
    }
}
