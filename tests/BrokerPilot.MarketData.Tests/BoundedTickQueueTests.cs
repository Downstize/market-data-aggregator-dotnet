using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;

namespace BrokerPilot.MarketData.Tests;

public sealed class BoundedTickQueueTests
{
    [Fact]
    public async Task Full_queue_applies_backpressure_until_consumer_frees_capacity()
    {
        var queue = new BoundedTickQueue(new MarketDataOptions { ChannelCapacity = 1 });
        await queue.EnqueueAsync(TestTicks.Create(1), CancellationToken.None);

        var blockedWrite = queue.EnqueueAsync(TestTicks.Create(2), CancellationToken.None).AsTask();
        var winner = await Task.WhenAny(blockedWrite, Task.Delay(50));

        winner.Should().NotBe(blockedWrite, "a bounded channel in Wait mode must backpressure the producer");
        queue.Count.Should().Be(1);

        var first = await queue.ReadAsync(CancellationToken.None);
        first.Should().NotBeNull();

        await blockedWrite.WaitAsync(TimeSpan.FromSeconds(1));
        queue.Count.Should().Be(1);
    }
}
