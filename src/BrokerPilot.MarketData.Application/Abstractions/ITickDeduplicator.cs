namespace BrokerPilot.MarketData.Application.Abstractions;

public interface ITickDeduplicator
{
    bool TryAccept(Guid tickId);

    int ApproximateEntryCount { get; }
}
