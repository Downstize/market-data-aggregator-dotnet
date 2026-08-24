using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Domain;
using BrokerPilot.MarketData.Infrastructure.Persistence;
using BrokerPilot.MarketData.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BrokerPilot.MarketData.Tests;

public sealed class ResilientTickBatchWriterTests
{
    [Fact]
    public async Task Transient_database_failure_is_retried_then_batch_goes_to_dead_letter_store()
    {
        var options = new MarketDataOptions
        {
            DatabaseMaxRetries = 2,
            DatabaseRetryInitialDelay = TimeSpan.Zero,
            DatabaseRetryMaxDelay = TimeSpan.Zero
        };
        var repository = Substitute.For<ITickRepository>();
        var deadLetterStore = Substitute.For<IDeadLetterStore>();
        var retryDelayProvider = Substitute.For<IDatabaseRetryDelayProvider>();
        var metrics = new AggregatorMetrics();
        var ticks = new[] { TestTicks.Create(1), TestTicks.Create(2) };

        repository.WriteBatchAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("database unavailable")));
        retryDelayProvider.GetDelay(Arg.Any<int>()).Returns(TimeSpan.Zero);
        deadLetterStore.WriteBatchAsync(
                Arg.Any<IReadOnlyList<NormalizedTick>>(),
                Arg.Any<Exception>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut(repository, deadLetterStore, retryDelayProvider, FakeTransientErrorDetector.Transient, metrics, options);

        await sut.WriteAsync(ticks, CancellationToken.None);

        await repository.Received(3).WriteBatchAsync(
            Arg.Any<IReadOnlyList<NormalizedTick>>(),
            Arg.Any<CancellationToken>());
        await deadLetterStore.Received(1).WriteBatchAsync(
            ticks,
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());

        var snapshot = metrics.Snapshot();
        snapshot.DatabaseWriteAttemptFailures.Should().Be(3);
        snapshot.TicksDeadLettered.Should().Be(2);
        snapshot.TicksDropped.Should().Be(0);
    }

    [Fact]
    public async Task Permanent_database_error_is_not_retried_and_goes_straight_to_dead_letter()
    {
        var options = new MarketDataOptions
        {
            DatabaseMaxRetries = 4,
            DatabaseRetryInitialDelay = TimeSpan.Zero,
            DatabaseRetryMaxDelay = TimeSpan.Zero
        };
        var repository = Substitute.For<ITickRepository>();
        var deadLetterStore = Substitute.For<IDeadLetterStore>();
        var retryDelayProvider = Substitute.For<IDatabaseRetryDelayProvider>();
        var metrics = new AggregatorMetrics();
        var ticks = new[] { TestTicks.Create(1), TestTicks.Create(2) };

        repository.WriteBatchAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("value too long for type character varying(32)")));
        deadLetterStore.WriteBatchAsync(
                Arg.Any<IReadOnlyList<NormalizedTick>>(),
                Arg.Any<Exception>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut(repository, deadLetterStore, retryDelayProvider, FakeTransientErrorDetector.Permanent, metrics, options);

        await sut.WriteAsync(ticks, CancellationToken.None);

        // Нарушение constraint ожиданием не лечится: бюджет ретраев должен остаться нетронутым
        await repository.Received(1).WriteBatchAsync(
            Arg.Any<IReadOnlyList<NormalizedTick>>(),
            Arg.Any<CancellationToken>());
        metrics.Snapshot().TicksDeadLettered.Should().Be(2);
    }

    [Fact]
    public async Task Cancelled_write_still_dead_letters_the_batch_before_rethrowing()
    {
        var options = new MarketDataOptions { DatabaseMaxRetries = 4 };
        var repository = Substitute.For<ITickRepository>();
        var deadLetterStore = Substitute.For<IDeadLetterStore>();
        var retryDelayProvider = Substitute.For<IDatabaseRetryDelayProvider>();
        var metrics = new AggregatorMetrics();
        var ticks = new[] { TestTicks.Create(1), TestTicks.Create(2), TestTicks.Create(3) };

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        repository.WriteBatchAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromCanceled<int>(cancellation.Token));
        deadLetterStore.WriteBatchAsync(
                Arg.Any<IReadOnlyList<NormalizedTick>>(),
                Arg.Any<Exception>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut(repository, deadLetterStore, retryDelayProvider, FakeTransientErrorDetector.Transient, metrics, options);

        // Форсированная остановка не должна превращать «уже принято от биржи» в "исчезло"
        await FluentActions.Awaiting(() => sut.WriteAsync(ticks, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        await deadLetterStore.Received(1).WriteBatchAsync(
            ticks,
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
        metrics.Snapshot().TicksDeadLettered.Should().Be(3);
        metrics.Snapshot().TicksDropped.Should().Be(0);
    }

    [Fact]
    public async Task Successful_idempotent_insert_counts_database_conflicts_separately()
    {
        var options = new MarketDataOptions { DatabaseMaxRetries = 1 };
        var repository = Substitute.For<ITickRepository>();
        var deadLetterStore = Substitute.For<IDeadLetterStore>();
        var retryDelayProvider = Substitute.For<IDatabaseRetryDelayProvider>();
        var metrics = new AggregatorMetrics();
        var ticks = new[] { TestTicks.Create(1), TestTicks.Create(2) };

        repository.WriteBatchAsync(ticks, Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));

        var sut = CreateSut(repository, deadLetterStore, retryDelayProvider, FakeTransientErrorDetector.Transient, metrics, options);

        await sut.WriteAsync(ticks, CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.TicksWritten.Should().Be(1);
        snapshot.DatabaseConflicts.Should().Be(1);
        snapshot.DatabaseWriteAttemptFailures.Should().Be(0);
        await deadLetterStore.DidNotReceive().WriteBatchAsync(
            Arg.Any<IReadOnlyList<NormalizedTick>>(),
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dead_letter_failure_is_not_silent_and_increments_dropped_counter()
    {
        var options = new MarketDataOptions { DatabaseMaxRetries = 0 };
        var repository = Substitute.For<ITickRepository>();
        var deadLetterStore = Substitute.For<IDeadLetterStore>();
        var retryDelayProvider = Substitute.For<IDatabaseRetryDelayProvider>();
        var metrics = new AggregatorMetrics();
        var ticks = new[] { TestTicks.Create(1), TestTicks.Create(2), TestTicks.Create(3) };

        repository.WriteBatchAsync(Arg.Any<IReadOnlyList<NormalizedTick>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(new InvalidOperationException("database unavailable")));
        deadLetterStore.WriteBatchAsync(
                Arg.Any<IReadOnlyList<NormalizedTick>>(),
                Arg.Any<Exception>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new IOException("disk unavailable")));

        var sut = CreateSut(repository, deadLetterStore, retryDelayProvider, FakeTransientErrorDetector.Transient, metrics, options);

        await sut.WriteAsync(ticks, CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.TicksDeadLettered.Should().Be(0);
        snapshot.TicksDropped.Should().Be(3);
    }

    private static ResilientTickBatchWriter CreateSut(
        ITickRepository repository,
        IDeadLetterStore deadLetterStore,
        IDatabaseRetryDelayProvider retryDelayProvider,
        ITransientDatabaseErrorDetector transientErrorDetector,
        IAggregatorMetrics metrics,
        MarketDataOptions options) =>
        new(
            repository,
            deadLetterStore,
            retryDelayProvider,
            transientErrorDetector,
            metrics,
            options,
            NullLogger<ResilientTickBatchWriter>.Instance);
}
