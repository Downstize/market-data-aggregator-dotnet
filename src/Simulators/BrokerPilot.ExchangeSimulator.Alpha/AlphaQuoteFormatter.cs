using System.Globalization;
using System.Text.Json;
using BrokerPilot.ExchangeSimulator.Common;

namespace BrokerPilot.ExchangeSimulator.Alpha;

public sealed class AlphaQuoteFormatter : IQuoteFormatter
{
    public string ExchangeName => "alpha";

    public byte[] Serialize(GeneratedQuote quote) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        symbol = quote.Symbol,
        price = quote.Price.ToString("G29", CultureInfo.InvariantCulture),
        volume = quote.Volume,
        timestamp = quote.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
    });
}
