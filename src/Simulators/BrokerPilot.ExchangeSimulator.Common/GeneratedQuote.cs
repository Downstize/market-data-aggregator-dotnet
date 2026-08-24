namespace BrokerPilot.ExchangeSimulator.Common;

public sealed record GeneratedQuote(
    string Symbol,
    decimal Price,
    decimal Volume,
    DateTimeOffset Timestamp);
