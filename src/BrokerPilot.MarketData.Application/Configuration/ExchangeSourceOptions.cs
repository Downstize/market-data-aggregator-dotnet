namespace BrokerPilot.MarketData.Application.Configuration;

public sealed class ExchangeSourceOptions
{
    public required string Name { get; init; }

    public required string Format { get; init; }

    public required string Url { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Exchange source Name is required");
        }

        if (string.IsNullOrWhiteSpace(Format))
        {
            throw new InvalidOperationException($"Exchange source '{Name}' must specify Format");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Exchange source '{Name}' has invalid WebSocket URL '{Url}'");
        }

        if (IdleTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Exchange source '{Name}' has invalid IdleTimeout");
        }
    }
}
