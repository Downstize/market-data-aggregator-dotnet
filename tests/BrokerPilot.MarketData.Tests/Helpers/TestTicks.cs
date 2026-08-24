using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal static class TestTicks
{
    public static NormalizedTick Create(
        int offsetMilliseconds = 0,
        string source = "alpha",
        string symbol = "EURUSD") =>
        NormalizedTick.Create(
            symbol,
            1.085m + (offsetMilliseconds / 1_000_000m),
            100_000m,
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(offsetMilliseconds),
            source,
            new DateTimeOffset(2026, 6, 1, 12, 0, 1, TimeSpan.Zero).AddMilliseconds(offsetMilliseconds));
}
