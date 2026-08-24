using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Infrastructure.Exchange;
using BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;
using BrokerPilot.MarketData.Infrastructure.Exchange.Transport;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerPilot.MarketData.Tests;

public sealed class WebSocketExchangeFeedTests
{
    [Fact]
    public async Task Feed_reconnects_multiple_times_after_disconnect_and_keeps_processing()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var factory = new ScriptedWebSocketClientFactory(new IWebSocketClient[]
        {
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.001Z")), ScriptedFrame.Close()]),
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.002Z")), ScriptedFrame.Close()]),
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.003Z"))])
        });
        var options = CreateOptions();
        var metrics = new AggregatorMetrics();
        var queue = new CapturingTickQueue(cancelAfter: 3, cancellation);
        var sut = CreateFeed("alpha", factory, queue, metrics, options);

        await sut.RunAsync(cancellation.Token);

        factory.CreatedCount.Should().Be(3);
        queue.Items.Should().HaveCount(3);
        metrics.Snapshot().ReconnectsScheduled.Should().Be(2);
        metrics.Snapshot().TicksAccepted.Should().Be(3);
    }

    [Fact]
    public async Task Replayed_tick_after_reconnect_is_removed_by_deduplication()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var duplicatePayload = AlphaPayload("2026-06-01T12:00:00.001Z");
        var uniquePayload = AlphaPayload("2026-06-01T12:00:00.002Z");
        var factory = new ScriptedWebSocketClientFactory(new IWebSocketClient[]
        {
            new ScriptedWebSocketClient([ScriptedFrame.Text(duplicatePayload), ScriptedFrame.Close()]),
            new ScriptedWebSocketClient([ScriptedFrame.Text(duplicatePayload), ScriptedFrame.Text(uniquePayload)])
        });
        var options = CreateOptions();
        var metrics = new AggregatorMetrics();
        var queue = new CapturingTickQueue(cancelAfter: 2, cancellation);
        var sut = CreateFeed("alpha", factory, queue, metrics, options);

        await sut.RunAsync(cancellation.Token);

        queue.Items.Should().HaveCount(2);
        metrics.Snapshot().DuplicateTicks.Should().Be(1);
        metrics.Snapshot().TicksAccepted.Should().Be(2);
    }

    /// <summary>
    /// Сценарий, названный в задании прямым текстом: один источник постоянно обрывается, а
    /// остальные обязаны продолжать работу. Оба фида делят одну очередь, один дедупликатор и
    /// один объект метрик - ровно так, как они связаны в проде
    /// </summary>
    [Fact]
    public async Task Broken_source_reconnects_without_taking_the_healthy_source_down()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var options = CreateOptions();
        var metrics = new AggregatorMetrics();

        // alpha отдаёт два тика за два оборванных подключения и затем замолкает;
        // beta отдаёт три тика по одному здоровому подключению. Итого пять
        var queue = new CapturingTickQueue(cancelAfter: 5, cancellation);

        var brokenFactory = new ScriptedWebSocketClientFactory(new IWebSocketClient[]
        {
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.001Z")), ScriptedFrame.Close()]),
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.002Z")), ScriptedFrame.Close()]),
            new SilentWebSocketClient()
        });
        var healthyFactory = new ScriptedWebSocketClientFactory(new IWebSocketClient[]
        {
            new ScriptedWebSocketClient(
            [
                ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:01.001Z")),
                ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:01.002Z")),
                ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:01.003Z"))
            ])
        });

        var deduplicator = new ConcurrentTickDeduplicator(options, TimeProvider.System);
        var brokenFeed = CreateFeed("alpha", brokenFactory, queue, metrics, options, deduplicator);
        var healthyFeed = CreateFeed("beta", healthyFactory, queue, metrics, options, deduplicator);

        await Task.WhenAll(
            brokenFeed.RunAsync(cancellation.Token),
            healthyFeed.RunAsync(cancellation.Token));

        var bySource = queue.Items
            .GroupBy(tick => tick.Source)
            .ToDictionary(group => group.Key, group => group.Count());

        bySource.Should().ContainKey("beta");
        bySource["beta"].Should().Be(3, "a failing source must not interrupt the others");

        bySource.Should().ContainKey("alpha");
        bySource["alpha"].Should().Be(2);

        brokenFactory.CreatedCount.Should().Be(3, "alpha reconnected twice");
        healthyFactory.CreatedCount.Should().Be(1, "beta never had to reconnect");
    }

    /// <summary>
    /// Формально живой сокет, переставший отдавать данные, должен быть разорван idle-watchdog'ом, а не висеть вечно
    /// </summary>
    [Fact]
    public async Task Stalled_but_open_socket_is_detected_by_idle_timeout_and_reconnected()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var options = CreateOptions();
        var queue = new CapturingTickQueue(cancelAfter: 1, cancellation);
        var metrics = new AggregatorMetrics();
        var factory = new ScriptedWebSocketClientFactory(new IWebSocketClient[]
        {
            new SilentWebSocketClient(),
            new ScriptedWebSocketClient([ScriptedFrame.Text(AlphaPayload("2026-06-01T12:00:00.001Z"))])
        });
        var sut = CreateFeed("alpha", factory, queue, metrics, options, idleTimeout: TimeSpan.FromMilliseconds(150));

        await sut.RunAsync(cancellation.Token);

        factory.CreatedCount.Should().Be(2);
        metrics.Snapshot().ReconnectsScheduled.Should().Be(1);
        queue.Items.Should().HaveCount(1);
    }

    private static MarketDataOptions CreateOptions() => new()
    {
        ReceiveBufferSize = 1024,
        MaxMessageBytes = 4096,
        DeduplicationWindow = TimeSpan.FromMinutes(2),
        DeduplicationCleanupEvery = 100,
        ReconnectStabilityThreshold = TimeSpan.FromSeconds(30)
    };

    private static WebSocketExchangeFeed CreateFeed(
        string name,
        IWebSocketClientFactory factory,
        ITickQueue queue,
        IAggregatorMetrics metrics,
        MarketDataOptions options,
        ITickDeduplicator? deduplicator = null,
        TimeSpan? idleTimeout = null) =>
        new(
            new ExchangeSourceOptions
            {
                Name = name,
                Format = "alpha-v1",
                Url = "ws://unit-test/ws",
                IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(5)
            },
            new AlphaExchangeMessageParser(),
            factory,
            deduplicator ?? new ConcurrentTickDeduplicator(options, TimeProvider.System),
            queue,
            metrics,
            new ZeroReconnectDelayProvider(),
            options,
            TimeProvider.System,
            NullLogger<WebSocketExchangeFeed>.Instance);

    private static string AlphaPayload(string timestamp) =>
        $$"""{"symbol":"EURUSD","price":"1.08525","volume":120000,"timestamp":"{{timestamp}}"}""";
}
