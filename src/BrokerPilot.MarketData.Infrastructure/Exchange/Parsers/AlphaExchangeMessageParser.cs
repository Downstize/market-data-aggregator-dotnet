using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;

public sealed class AlphaExchangeMessageParser : IExchangeMessageParser
{
    public string FormatId => "alpha-v1";

    public TickParseResult Parse(ReadOnlyMemory<byte> payload, string source, DateTimeOffset receivedAt)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<AlphaQuote>(payload.Span);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Symbol) || string.IsNullOrWhiteSpace(dto.Price))
            {
                return TickParseResult.Failure("Alpha payload misses symbol or price");
            }

            if (!decimal.TryParse(
                    dto.Price,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var price))
            {
                return TickParseResult.Failure($"Alpha price '{dto.Price}' is invalid");
            }

            var tick = NormalizedTick.Create(
                dto.Symbol,
                price,
                dto.Volume,
                dto.Timestamp,
                source,
                receivedAt);

            return TickParseResult.Success(tick);
        }
        catch (JsonException exception)
        {
            return TickParseResult.Failure($"Alpha JSON is invalid: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return TickParseResult.Failure($"Alpha tick is invalid: {exception.Message}");
        }
    }

    private sealed record AlphaQuote
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("price")]
        public string? Price { get; init; }

        [JsonPropertyName("volume")]
        public decimal Volume { get; init; }

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; init; }
    }
}
