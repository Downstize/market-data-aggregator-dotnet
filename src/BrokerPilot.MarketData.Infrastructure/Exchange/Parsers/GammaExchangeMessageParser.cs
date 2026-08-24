using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;

public sealed class GammaExchangeMessageParser : IExchangeMessageParser
{
    private const string TimestampFormat = "yyyyMMdd HH:mm:ss.fff";

    public string FormatId => "gamma-v1";

    public TickParseResult Parse(ReadOnlyMemory<byte> payload, string source, DateTimeOffset receivedAt)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<GammaQuote>(payload.Span);
            if (dto?.Instrument is null || string.IsNullOrWhiteSpace(dto.Instrument.Ticker) || string.IsNullOrWhiteSpace(dto.Time))
            {
                return TickParseResult.Failure("Gamma payload misses instrument.ticker or time");
            }

            if (!DateTimeOffset.TryParseExact(
                    dto.Time,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                return TickParseResult.Failure($"Gamma time '{dto.Time}' is invalid");
            }

            var tick = NormalizedTick.Create(
                dto.Instrument.Ticker,
                dto.Last,
                dto.Size,
                timestamp,
                source,
                receivedAt);

            return TickParseResult.Success(tick);
        }
        catch (JsonException exception)
        {
            return TickParseResult.Failure($"Gamma JSON is invalid: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return TickParseResult.Failure($"Gamma tick is invalid: {exception.Message}");
        }
    }

    private sealed record GammaQuote
    {
        [JsonPropertyName("instrument")]
        public InstrumentQuote? Instrument { get; init; }

        [JsonPropertyName("last")]
        public decimal Last { get; init; }

        [JsonPropertyName("size")]
        public decimal Size { get; init; }

        [JsonPropertyName("time")]
        public string? Time { get; init; }
    }

    private sealed record InstrumentQuote
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; init; }
    }
}
