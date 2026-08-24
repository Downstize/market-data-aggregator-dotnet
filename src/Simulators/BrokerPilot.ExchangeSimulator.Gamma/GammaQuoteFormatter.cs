using System.Globalization;
using System.Text.Json;
using BrokerPilot.ExchangeSimulator.Common;

namespace BrokerPilot.ExchangeSimulator.Gamma;

public sealed class GammaQuoteFormatter : IQuoteFormatter
{
    public string ExchangeName => "gamma";

    public byte[] Serialize(GeneratedQuote quote) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        instrument = new
        {
            ticker = quote.Symbol
        },
        last = quote.Price,
        size = quote.Volume,
        time = quote.Timestamp.ToUniversalTime().ToString("yyyyMMdd HH:mm:ss.fff", CultureInfo.InvariantCulture)
    });
}
