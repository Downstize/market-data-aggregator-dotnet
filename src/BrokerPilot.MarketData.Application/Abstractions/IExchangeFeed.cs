namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IExchangeFeed
{
    string Name { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
