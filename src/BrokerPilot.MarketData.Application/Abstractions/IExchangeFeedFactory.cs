using BrokerPilot.MarketData.Application.Configuration;

namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IExchangeFeedFactory
{
    IExchangeFeed Create(ExchangeSourceOptions source);
}
