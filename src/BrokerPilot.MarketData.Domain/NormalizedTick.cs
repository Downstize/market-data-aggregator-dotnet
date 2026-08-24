namespace BrokerPilot.MarketData.Domain;

public sealed record NormalizedTick(
    Guid Id,
    string Symbol,
    decimal Price,
    decimal Volume,
    DateTimeOffset Timestamp,
    string Source,
    DateTimeOffset ReceivedAt)
{
    public static NormalizedTick Create(
        string symbol,
        decimal price,
        decimal volume,
        DateTimeOffset timestamp,
        string source,
        DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Ticker must not be empty", nameof(symbol));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source must not be empty", nameof(source));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive");
        }

        if (volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must not be negative");
        }

        if (timestamp == default)
        {
            throw new ArgumentException("Timestamp must be specified", nameof(timestamp));
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var normalizedSource = source.Trim().ToLowerInvariant();
        var normalizedTimestamp = timestamp.ToUniversalTime();
        var normalizedReceivedAt = receivedAt.ToUniversalTime();
        var id = TickIdentity.Create(
            normalizedSource,
            normalizedSymbol,
            price,
            volume,
            normalizedTimestamp);

        return new NormalizedTick(
            id,
            normalizedSymbol,
            price,
            volume,
            normalizedTimestamp,
            normalizedSource,
            normalizedReceivedAt);
    }
}
