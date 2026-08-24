using System.Diagnostics;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Domain;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Application.Services;

public sealed class TickBatchConsumer(
    ITickQueue queue,
    ITickBatchWriter writer,
    IAggregatorMetrics metrics,
    MarketDataOptions options,
    ILogger<TickBatchConsumer> logger) : ITickBatchConsumer
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var batch = new List<NormalizedTick>(options.BatchSize);

        // Один CancellationTokenSource переиспользуется между чтениями вместо создания linked-
        // источника (и регистрации в очереди таймеров) на каждый тик. При ~1000 тиков/сек
        // вариант «на каждый тик» сжигал бы 1000 CTS и 1000 таймеров в секунду впустую
        CancellationTokenSource? flushTimeout = null;

        try
        {
            while (true)
            {
                var firstTick = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (firstTick is null)
                {
                    return;
                }

                batch.Add(firstTick);
                var queueCompleted = false;
                var startedAt = Stopwatch.GetTimestamp();

                while (batch.Count < options.BatchSize)
                {
                    while (batch.Count < options.BatchSize && queue.TryRead(out var bufferedTick))
                    {
                        batch.Add(bufferedTick);
                    }

                    if (batch.Count >= options.BatchSize)
                    {
                        break;
                    }

                    var remaining = options.FlushInterval - Stopwatch.GetElapsedTime(startedAt);
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    // TryReset возвращает false, только если источник уже отменён - а это ровно
                    // тот случай, когда новый linked-источник действительно нужен
                    if (flushTimeout is null || !flushTimeout.TryReset())
                    {
                        flushTimeout?.Dispose();
                        flushTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    }

                    flushTimeout.CancelAfter(remaining);

                    try
                    {
                        var nextTick = await queue.ReadAsync(flushTimeout.Token).ConfigureAwait(false);
                        if (nextTick is null)
                        {
                            queueCompleted = true;
                            break;
                        }

                        batch.Add(nextTick);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Истекло окно сброса: пишем то, что успели набрать
                        break;
                    }
                }

                try
                {
                    await writer.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // С момента передачи writer'у судьба этих тиков (записаны / уехали в
                    // dead-letter / явно потеряны) — его ответственность. Очистка здесь, в том
                    // числе на пути исключения, исключает двойной учёт ниже
                    batch.Clear();
                }

                if (queueCompleted)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Форсированная остановка: drain не уложился в таймаут. То, что осталось в работе,
            // теряется — но теряется громко и с точным числом.
            AccountForAbandonedTicks(batch);
            throw;
        }
        finally
        {
            flushTimeout?.Dispose();
        }
    }

    private void AccountForAbandonedTicks(List<NormalizedTick> batch)
    {
        var abandoned = batch.Count;
        batch.Clear();

        while (queue.TryRead(out _))
        {
            abandoned++;
        }

        if (abandoned == 0)
        {
            return;
        }

        metrics.TicksDropped(abandoned);
        logger.LogCritical(
            "Forced shutdown: {Count} ticks were abandoned before they could be persisted. " +
            "They are counted in TicksDropped, not silently discarded.",
            abandoned);
    }
}
