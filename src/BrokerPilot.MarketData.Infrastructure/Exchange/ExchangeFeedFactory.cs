using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Infrastructure.Exchange.Transport;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Infrastructure.Exchange;

public sealed class ExchangeFeedFactory(
    ExchangeMessageParserResolver parserResolver,
    IWebSocketClientFactory clientFactory,
    ITickDeduplicator deduplicator,
    ITickQueue queue,
    IAggregatorMetrics metrics,
    IReconnectDelayProvider reconnectDelayProvider,
    MarketDataOptions options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IExchangeFeedFactory
{
    public IExchangeFeed Create(ExchangeSourceOptions source)
    {
        var parser = parserResolver.Resolve(source.Format);

        return new WebSocketExchangeFeed(
            source,
            parser,
            clientFactory,
            deduplicator,
            queue,
            metrics,
            reconnectDelayProvider,
            options,
            timeProvider,
            loggerFactory.CreateLogger<WebSocketExchangeFeed>());
    }
}
