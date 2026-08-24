namespace BrokerPilot.ExchangeSimulator.Common;

public interface IQuoteFormatter
{
    string ExchangeName { get; }

    byte[] Serialize(GeneratedQuote quote);
}
