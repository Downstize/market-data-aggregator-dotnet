namespace BrokerPilot.ExchangeSimulator.Common;

public sealed class MarketQuoteGenerator
{
    private readonly object _gate = new();
    private static readonly string[] Symbols = ["EURUSD", "GBPUSD", "USDJPY", "XAUUSD", "BTCUSD"];

    private readonly Dictionary<string, decimal> _prices = new(StringComparer.Ordinal)
    {
        ["EURUSD"] = 1.08500m,
        ["GBPUSD"] = 1.27200m,
        ["USDJPY"] = 147.25000m,
        ["XAUUSD"] = 2_405.00000m,
        ["BTCUSD"] = 64_500.00000m
    };

    public GeneratedQuote Next()
    {
        lock (_gate)
        {
            var symbol = Symbols[Random.Shared.Next(Symbols.Length)];
            var current = _prices[symbol];
            var relativeMove = ((decimal)Random.Shared.NextDouble() - 0.5m) * 0.0004m;
            var next = decimal.Round(Math.Max(0.00001m, current * (1 + relativeMove)), 5);
            _prices[symbol] = next;

            var volume = Random.Shared.Next(1, 501) * 1_000m;
            return new GeneratedQuote(symbol, next, volume, DateTimeOffset.UtcNow);
        }
    }
}
