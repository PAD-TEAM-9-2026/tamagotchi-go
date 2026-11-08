using Microsoft.AspNetCore.Http;
using Xunit;

namespace Tamagotchi.Discovery.Tests;

public sealed class LoadCounterTests
{
    [Fact]
    public void An_idle_counter_is_all_zero()
    {
        var load = new LoadCounter(new ManualTimeProvider());

        Assert.Equal(new LoadSnapshot(0, 0, 0), load.Snapshot());
    }

    [Fact]
    public void It_counts_requests_in_flight_and_finished_with_their_average()
    {
        var load = new LoadCounter(new ManualTimeProvider());
        load.Started();
        load.Started();
        load.Started();
        load.Finished(TimeSpan.FromMilliseconds(10));
        load.Finished(TimeSpan.FromMilliseconds(20.25));

        Assert.Equal(new LoadSnapshot(1, 2, 15.1), load.Snapshot());
    }

    [Fact]
    public void Requests_older_than_a_minute_drop_out()
    {
        var time = new ManualTimeProvider();
        var load = new LoadCounter(time);
        load.Started();
        load.Finished(TimeSpan.FromMilliseconds(100));
        time.Advance(TimeSpan.FromSeconds(30));
        load.Started();
        load.Finished(TimeSpan.FromMilliseconds(50));

        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(new LoadSnapshot(0, 1, 50), load.Snapshot());
    }

    [Fact]
    public void In_flight_never_goes_below_zero()
    {
        var load = new LoadCounter(new ManualTimeProvider());

        load.Finished(TimeSpan.Zero);

        Assert.Equal(0, load.Snapshot().InFlightRequests);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    [InlineData("/status")]
    [InlineData("/STATUS")]
    public async Task The_probes_are_not_counted(string path)
    {
        var time = new ManualTimeProvider();
        var load = new LoadCounter(time);
        var middleware = new LoadCounterMiddleware(_ => Task.CompletedTask, load, time);

        await middleware.InvokeAsync(new DefaultHttpContext { Request = { Path = path } });

        Assert.Equal(0, load.Snapshot().RequestsLastMinute);
    }

    [Fact]
    public async Task Other_requests_are_counted_with_their_duration_even_when_they_fail()
    {
        var time = new ManualTimeProvider();
        var load = new LoadCounter(time);
        LoadSnapshot? during = null;
        var middleware = new LoadCounterMiddleware(
            _ =>
            {
                during = load.Snapshot();
                time.Advance(TimeSpan.FromMilliseconds(40));
                throw new InvalidOperationException("boom");
            },
            load,
            time);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(new DefaultHttpContext { Request = { Path = "/v1/users" } }));

        Assert.Equal(1, during?.InFlightRequests);
        Assert.Equal(new LoadSnapshot(0, 1, 40), load.Snapshot());
    }
}
