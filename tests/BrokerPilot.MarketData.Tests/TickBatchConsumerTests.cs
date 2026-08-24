using System.Collections.Concurrent;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Domain;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BrokerPilot.MarketData.Tests;

public sealed class TickBatchConsumerTests
{
    [Fact]
    public async Task Completed_queue_flushes_full_batch_and_remaining_partial_batch()
    {
        var options = new MarketDataOptions
        {
            ChannelCapacity = 10,
            BatchSize = 3,
            FlushInterval = TimeSpan.FromSeconds(10)
        };
        var queue = new BoundedTickQueue(options);
        var writer = Substitute.For<ITickBatchWriter>();
        var batchSizes = new ConcurrentQueue<int>();

        writer.WriteAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                batchSizes.Enqueue(callInfo.Arg<IReadOnlyList<NormalizedTick>>().Count);
                return Task.CompletedTask;
            });

        for (var index = 0; index < 4; index++)
        {
            await queue.EnqueueAsync(TestTicks.Create(index), CancellationToken.None);
        }

        queue.Complete();
        var sut = new TickBatchConsumer(queue, writer, new AggregatorMetrics(), options, NullLogger<TickBatchConsumer>.Instance);

        await sut.RunAsync(CancellationToken.None);

        batchSizes.Should().Equal(3, 1);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Forced_shutdown_counts_every_abandoned_tick_exactly_once()
    {
        var options = new MarketDataOptions
        {
            ChannelCapacity = 100,
            BatchSize = 3,
            FlushInterval = TimeSpan.FromSeconds(10)
        };
        var queue = new BoundedTickQueue(options);
        var writer = Substitute.For<ITickBatchWriter>();
        var metrics = new AggregatorMetrics();
        using var cancellation = new CancellationTokenSource();

        // Повторяет поведение настоящего writer'а при форсированной остановке: он берёт батч на
        // себя (кладёт в dead-letter) и только потом пробрасывает отмену дальше
        writer.WriteAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException(new OperationCanceledException(cancellation.Token));
            });

        for (var index = 0; index < 10; index++)
        {
            await queue.EnqueueAsync(TestTicks.Create(index), CancellationToken.None);
        }

        var sut = new TickBatchConsumer(queue, writer, metrics, options, NullLogger<TickBatchConsumer>.Instance);

        await FluentActions.Awaiting(() => sut.RunAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        // 3 тика ушли writer'у и находятся под его ответственностью; остальные 7 так и не вышли
        // из очереди и должны быть учтены как потерянные - без двойного счёта и без замалчивания
        metrics.Snapshot().TicksDropped.Should().Be(7);
        queue.Count.Should().Be(0);
    }
}
