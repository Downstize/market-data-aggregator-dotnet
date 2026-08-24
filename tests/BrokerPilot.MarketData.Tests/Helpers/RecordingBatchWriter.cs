using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class RecordingBatchWriter : ITickBatchWriter
{
    private readonly List<NormalizedTick> _ticks = [];
    private readonly List<int> _batchSizes = [];
    private readonly object _gate = new();

    public IReadOnlyList<NormalizedTick> Ticks
    {
        get
        {
            lock (_gate)
            {
                return _ticks.ToArray();
            }
        }
    }

    public IReadOnlyList<int> BatchSizes
    {
        get
        {
            lock (_gate)
            {
                return _batchSizes.ToArray();
            }
        }
    }

    public Task WriteAsync(IReadOnlyList<NormalizedTick> ticks, CancellationToken cancellationToken)
    {
        // Копируем немедленно: consumer переиспользует список батча и очищает его сразу после
        // возврата из этого метода.
        lock (_gate)
        {
            _ticks.AddRange(ticks);
            _batchSizes.Add(ticks.Count);
        }

        return Task.CompletedTask;
    }
}
