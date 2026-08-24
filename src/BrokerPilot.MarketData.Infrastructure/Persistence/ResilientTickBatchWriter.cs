using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Domain;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Infrastructure.Persistence;

public sealed class ResilientTickBatchWriter(
    ITickRepository repository,
    IDeadLetterStore deadLetterStore,
    IDatabaseRetryDelayProvider retryDelayProvider,
    ITransientDatabaseErrorDetector transientErrorDetector,
    IAggregatorMetrics metrics,
    MarketDataOptions options,
    ILogger<ResilientTickBatchWriter> logger) : ITickBatchWriter
{
    public async Task WriteAsync(
        IReadOnlyList<NormalizedTick> ticks,
        CancellationToken cancellationToken)
    {
        if (ticks.Count == 0)
        {
            return;
        }

        Exception? lastException = null;

        for (var attempt = 0; attempt <= options.DatabaseMaxRetries; attempt++)
        {
            try
            {
                var inserted = await repository.WriteBatchAsync(ticks, cancellationToken).ConfigureAwait(false);
                metrics.TicksWritten(inserted);
                metrics.DatabaseConflict(ticks.Count - inserted);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Отмена операции не отменяет обязательство: эти тики уже сняты с сокета, значит
                // они уходят в dead-letter до того, как исключение будет проброшено дальше.
                // CancellationToken.None здесь осознанно - смысл этой записи ровно в том, чтобы
                // она завершилась, пока всё остальное останавливается
                await PersistToDeadLetterAsync(
                        ticks,
                        lastException ?? new OperationCanceledException(
                            "Database write was cancelled by a forced shutdown"))
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                metrics.DatabaseWriteAttemptFailed();

                if (!transientErrorDetector.IsTransient(exception))
                {
                    // Нарушение constraint, отсутствующая таблица, значение не влезает в тип:
                    // ожидание тут не поможет. Ретраи только оттянут запись в dead-letter
                    logger.LogError(
                        exception,
                        "Permanent database error for a batch of {Count} ticks. Skipping retries and dead-lettering immediately",
                        ticks.Count);
                    break;
                }

                if (attempt >= options.DatabaseMaxRetries)
                {
                    break;
                }

                var retryNumber = attempt + 1;
                var delay = retryDelayProvider.GetDelay(retryNumber);
                logger.LogWarning(
                    exception,
                    "Transient database batch write failure. Retry {RetryNumber}/{MaxRetries} in {DelayMs} ms",
                    retryNumber,
                    options.DatabaseMaxRetries,
                    delay.TotalMilliseconds);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await PersistToDeadLetterAsync(ticks, exception).ConfigureAwait(false);
                    throw;
                }
            }
        }

        await PersistToDeadLetterAsync(
                ticks,
                lastException ?? new InvalidOperationException("Database batch write failed without an exception"))
            .ConfigureAwait(false);
    }

    private async Task PersistToDeadLetterAsync(IReadOnlyList<NormalizedTick> ticks, Exception reason)
    {
        try
        {
            await deadLetterStore.WriteBatchAsync(ticks, reason, CancellationToken.None).ConfigureAwait(false);
            metrics.TicksDeadLettered(ticks.Count);
        }
        catch (Exception deadLetterException)
        {
            metrics.TicksDropped(ticks.Count);
            logger.LogCritical(
                deadLetterException,
                "CRITICAL: database write and dead-letter persistence both failed. {Count} ticks were dropped and accounted for explicitly",
                ticks.Count);
        }
    }
}
