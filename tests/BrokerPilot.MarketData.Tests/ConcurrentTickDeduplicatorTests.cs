using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;

namespace BrokerPilot.MarketData.Tests;

public sealed class ConcurrentTickDeduplicatorTests
{
    [Fact]
    public void Concurrent_calls_for_same_tick_accept_exactly_one()
    {
        var options = new MarketDataOptions
        {
            DeduplicationWindow = TimeSpan.FromMinutes(2),
            DeduplicationCleanupEvery = 100
        };
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var sut = new ConcurrentTickDeduplicator(options, timeProvider);
        var tickId = Guid.NewGuid();
        var accepted = 0;

        Parallel.For(0, 10_000, _ =>
        {
            if (sut.TryAccept(tickId))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        accepted.Should().Be(1);
    }

    [Fact]
    public void Same_tick_is_accepted_again_after_deduplication_window_expires()
    {
        var options = new MarketDataOptions
        {
            DeduplicationWindow = TimeSpan.FromSeconds(10),
            DeduplicationCleanupEvery = 1
        };
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var sut = new ConcurrentTickDeduplicator(options, timeProvider);
        var tickId = Guid.NewGuid();

        sut.TryAccept(tickId).Should().BeTrue();
        sut.TryAccept(tickId).Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(11));

        sut.TryAccept(tickId).Should().BeTrue();
    }

    [Fact]
    public void Wall_clock_adjustment_does_not_change_deduplication_window()
    {
        var options = new MarketDataOptions
        {
            DeduplicationWindow = TimeSpan.FromSeconds(10),
            DeduplicationCleanupEvery = 100
        };
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var sut = new ConcurrentTickDeduplicator(options, timeProvider);
        var tickId = Guid.NewGuid();

        sut.TryAccept(tickId).Should().BeTrue();

        // Имитируем корректировку системных часов на час вперёд без прохождения монотонного времени
        timeProvider.AdjustUtcNow(TimeSpan.FromHours(1));

        sut.TryAccept(tickId).Should().BeFalse(
            "TTL дедупликации должен измеряться монотонным временем, а не системными часами");

        timeProvider.Advance(TimeSpan.FromSeconds(11));

        sut.TryAccept(tickId).Should().BeTrue();
    }
}
