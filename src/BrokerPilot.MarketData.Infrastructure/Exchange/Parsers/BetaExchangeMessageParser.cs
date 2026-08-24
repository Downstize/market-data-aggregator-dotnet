using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;

public sealed class BetaExchangeMessageParser : IExchangeMessageParser
{
    public string FormatId => "beta-v1";

    public TickParseResult Parse(ReadOnlyMemory<byte> payload, string source, DateTimeOffset receivedAt)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<BetaQuote>(payload.Span);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Symbol) || string.IsNullOrWhiteSpace(dto.Quantity))
            {
                return TickParseResult.Failure("Beta payload misses s or q");
            }

            if (!decimal.TryParse(
                    dto.Quantity,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var volume))
            {
                return TickParseResult.Failure($"Beta quantity '{dto.Quantity}' is invalid");
            }

            DateTimeOffset timestamp;
            try
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.UnixMilliseconds);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return TickParseResult.Failure($"Beta timestamp is invalid: {exception.Message}");
            }

            var tick = NormalizedTick.Create(
                dto.Symbol,
                dto.Price,
                volume,
                timestamp,
                source,
                receivedAt);

            return TickParseResult.Success(tick);
        }
        catch (JsonException exception)
        {
            return TickParseResult.Failure($"Beta JSON is invalid: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return TickParseResult.Failure($"Beta tick is invalid: {exception.Message}");
        }
    }

    private sealed record BetaQuote
    {
        [JsonPropertyName("s")]
        public string? Symbol { get; init; }

        [JsonPropertyName("p")]
        public decimal Price { get; init; }

        [JsonPropertyName("q")]
        public string? Quantity { get; init; }

        [JsonPropertyName("ts")]
        public long UnixMilliseconds { get; init; }
    }
}
