using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Application.Hosting;

/// <summary>
/// Периодический однострочный снимок состояния пайплайна
/// </summary>
public sealed class MetricsHeartbeatHostedService(
    IAggregatorMetrics metrics,
    ITickQueue queue,
    ITickDeduplicator deduplicator,
    MarketDataOptions options,
    ILogger<MetricsHeartbeatHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.MetricsLogInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var snapshot = metrics.Snapshot();
                var fillRatio = queue.Capacity == 0
                    ? 0d
                    : (double)queue.Count / queue.Capacity;

                logger.LogInformation(
                    "pipeline raw={Raw} accepted={Accepted} dup={Duplicates} invalid={Invalid} " +
                    "written={Written} conflicts={Conflicts} dbAttemptFailures={DbFailures} " +
                    "deadLettered={DeadLettered} dropped={Dropped} reconnects={Reconnects} " +
                    "queue={QueueDepth}/{QueueCapacity} ({FillRatio:P1}) dedupKeys={DedupKeys} sources={Sources}",
                    snapshot.RawTicksReceived,
                    snapshot.TicksAccepted,
                    snapshot.DuplicateTicks,
                    snapshot.InvalidTicks,
                    snapshot.TicksWritten,
                    snapshot.DatabaseConflicts,
                    snapshot.DatabaseWriteAttemptFailures,
                    snapshot.TicksDeadLettered,
                    snapshot.TicksDropped,
                    snapshot.ReconnectsScheduled,
                    queue.Count,
                    queue.Capacity,
                    fillRatio,
                    deduplicator.ApproximateEntryCount,
                    string.Join(
                        ',',
                        snapshot.SourceConnections.Select(pair => $"{pair.Key}={(pair.Value ? "up" : "down")}")));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка
        }
    }
}
