namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IExchangeMessageParser
{
    string FormatId { get; }

    TickParseResult Parse(ReadOnlyMemory<byte> payload, string source, DateTimeOffset receivedAt);
}
