using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Abstractions;

public interface ITickRepository
{
    Task<int> WriteBatchAsync(
        IReadOnlyList<NormalizedTick> ticks,
        CancellationToken cancellationToken);
}
