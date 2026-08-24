using System.Text.Json;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Domain;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Infrastructure.Persistence;

public sealed class FileDeadLetterStore : IDeadLetterStore, IDisposable
{
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileDeadLetterStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileDeadLetterStore(
        MarketDataOptions options,
        TimeProvider timeProvider,
        ILogger<FileDeadLetterStore> logger)
    {
        _directory = Path.GetFullPath(options.DeadLetterDirectory);
        _timeProvider = timeProvider;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public async Task WriteBatchAsync(
        IReadOnlyList<NormalizedTick> ticks,
        Exception reason,
        CancellationToken cancellationToken)
    {
        if (ticks.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(_directory);
            var failedAt = _timeProvider.GetUtcNow();
            var path = Path.Combine(_directory, $"failed-ticks-{failedAt:yyyyMMdd}.ndjson");

            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            await using var writer = new StreamWriter(stream);

            foreach (var tick in ticks)
            {
                var record = new DeadLetterRecord(
                    tick,
                    failedAt,
                    reason.GetType().Name,
                    reason.Message);
                var json = JsonSerializer.Serialize(record);
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogError(
                "Persisted {Count} ticks to dead-letter file {Path} after database retries were exhausted",
                ticks.Count,
                path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record DeadLetterRecord(
        NormalizedTick Tick,
        DateTimeOffset FailedAt,
        string ErrorType,
        string ErrorMessage);
}
