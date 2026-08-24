using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IDeadLetterStore
{
    Task WriteBatchAsync(
        IReadOnlyList<NormalizedTick> ticks,
        Exception reason,
        CancellationToken cancellationToken);
}
