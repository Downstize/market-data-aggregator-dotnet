namespace BrokerPilot.MarketData.Application.Abstractions;

public interface ITickBatchConsumer
{
    Task RunAsync(CancellationToken cancellationToken);
}
