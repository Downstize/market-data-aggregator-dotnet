using System.Globalization;
using System.Text.Json;
using BrokerPilot.ExchangeSimulator.Common;

namespace BrokerPilot.ExchangeSimulator.Beta;

public sealed class BetaQuoteFormatter : IQuoteFormatter
{
    public string ExchangeName => "beta";

    public byte[] Serialize(GeneratedQuote quote) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        s = quote.Symbol,
        p = quote.Price,
        q = quote.Volume.ToString("G29", CultureInfo.InvariantCulture),
        ts = quote.Timestamp.ToUnixTimeMilliseconds()
    });
}
