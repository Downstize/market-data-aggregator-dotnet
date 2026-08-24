using BrokerPilot.MarketData.Application.Abstractions;

namespace BrokerPilot.MarketData.Infrastructure.Exchange;

public sealed class ExchangeMessageParserResolver
{
    private readonly IReadOnlyDictionary<string, IExchangeMessageParser> _parsers;

    public ExchangeMessageParserResolver(IEnumerable<IExchangeMessageParser> parsers)
    {
        _parsers = parsers.ToDictionary(
            parser => parser.FormatId,
            parser => parser,
            StringComparer.OrdinalIgnoreCase);
    }

    public IExchangeMessageParser Resolve(string formatId)
    {
        if (_parsers.TryGetValue(formatId, out var parser))
        {
            return parser;
        }

        throw new InvalidOperationException(
            $"No exchange message parser is registered for format '{formatId}'");
    }
}
