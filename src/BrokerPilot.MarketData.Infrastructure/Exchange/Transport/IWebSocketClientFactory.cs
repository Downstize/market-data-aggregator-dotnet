namespace BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

public interface IWebSocketClientFactory
{
    IWebSocketClient Create();
}
