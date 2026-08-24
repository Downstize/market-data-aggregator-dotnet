using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Hosting;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerPilot.MarketData.Tests;

public sealed class MarketDataPipelineHostedServiceTests
{
    /// <summary>
    /// Порядок остановки - самое рискованное место решения: сначала останавливается приём,
    /// только потом закрывается канал, и лишь после этого батчер доводит запись до конца на
    /// собственном токене. Ошибка в порядке теряет всё, что накоплено в памяти, - причём молча,
    /// а это ровно тот тип отказа, который задание называет недопустимым
    /// </summary>
    [Fact]
    public async Task Graceful_stop_drains_every_accepted_tick_before_returning()
    {
        const int ticksPerFeed = 25;

        var options = CreateOptions(TimeSpan.FromSeconds(10));
        var queue = new BoundedTickQueue(options);
        var writer = new RecordingBatchWriter();
        var metrics = new AggregatorMetrics();
        var consumer = new TickBatchConsumer(queue, writer, metrics, options, NullLogger<TickBatchConsumer>.Instance);
        var feedFactory = new ScriptedExchangeFeedFactory(queue, ticksPerFeed);
        using var lifetime = new FakeHostApplicationLifetime();

        using var sut = new MarketDataPipelineHostedService(
            options,
            feedFactory,
            queue,
            consumer,
            lifetime,
            NullLogger<MarketDataPipelineHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        await Task.WhenAll(feedFactory.Feeds.Select(feed => feed.AllEnqueued))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await sut.StopAsync(CancellationToken.None);

        writer.Ticks.Should().HaveCount(ticksPerFeed * options.Sources.Count);
        queue.Count.Should().Be(0);
        metrics.Snapshot().TicksDropped.Should().Be(0, "a graceful stop must not lose accepted ticks");
        lifetime.StopApplicationCalls.Should().Be(0, "an orderly shutdown is not a crash");
    }

    [Fact]
    public async Task Stopping_twice_is_a_no_op_rather_than_a_second_shutdown()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(10));
        var queue = new BoundedTickQueue(options);
        var writer = new RecordingBatchWriter();
        var consumer = new TickBatchConsumer(queue, writer, new AggregatorMetrics(), options, NullLogger<TickBatchConsumer>.Instance);
        var feedFactory = new ScriptedExchangeFeedFactory(queue, ticksPerFeed: 5);
        using var lifetime = new FakeHostApplicationLifetime();

        using var sut = new MarketDataPipelineHostedService(
            options,
            feedFactory,
            queue,
            consumer,
            lifetime,
            NullLogger<MarketDataPipelineHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.WhenAll(feedFactory.Feeds.Select(feed => feed.AllEnqueued))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await sut.StopAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        writer.Ticks.Should().HaveCount(10);
    }

    private static MarketDataOptions CreateOptions(TimeSpan drainTimeout) => new()
    {
        ChannelCapacity = 500,
        BatchSize = 10,
        FlushInterval = TimeSpan.FromMilliseconds(50),
        DrainTimeout = drainTimeout,
        Sources =
        [
            new ExchangeSourceOptions { Name = "alpha", Format = "alpha-v1", Url = "ws://unit-test/alpha" },
            new ExchangeSourceOptions { Name = "beta", Format = "beta-v1", Url = "ws://unit-test/beta" }
        ]
    };
}
