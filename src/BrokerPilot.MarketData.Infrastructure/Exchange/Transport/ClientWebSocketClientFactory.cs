namespace BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

public sealed class ClientWebSocketClientFactory : IWebSocketClientFactory
{
    public IWebSocketClient Create() => new ClientWebSocketClient();
}
