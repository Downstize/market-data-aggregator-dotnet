using System.Text;
using BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;
using FluentAssertions;

namespace BrokerPilot.MarketData.Tests;

public sealed class ExchangeMessageParserTests
{
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 6, 1, 12, 0, 1, TimeSpan.Zero);

    [Fact]
    public void Alpha_parser_normalizes_string_price_and_iso_timestamp()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"symbol":"eurusd","price":"1.08525","volume":120000,"timestamp":"2026-06-01T12:00:00.123Z"}""");

        var result = new AlphaExchangeMessageParser().Parse(payload, "ALPHA", ReceivedAt);

        result.IsSuccess.Should().BeTrue();
        result.Tick!.Symbol.Should().Be("EURUSD");
        result.Tick.Source.Should().Be("alpha");
        result.Tick.Price.Should().Be(1.08525m);
        result.Tick.Volume.Should().Be(120000m);
        result.Tick.Timestamp.Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, 123, TimeSpan.Zero));
    }

    [Fact]
    public void Beta_parser_normalizes_short_fields_string_volume_and_unix_milliseconds()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, 456, TimeSpan.Zero);
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"s":"GBPUSD","p":1.2725,"q":"250000","ts":{{timestamp.ToUnixTimeMilliseconds()}}}""");

        var result = new BetaExchangeMessageParser().Parse(payload, "beta", ReceivedAt);

        result.IsSuccess.Should().BeTrue();
        result.Tick!.Symbol.Should().Be("GBPUSD");
        result.Tick.Price.Should().Be(1.2725m);
        result.Tick.Volume.Should().Be(250000m);
        result.Tick.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void Gamma_parser_normalizes_nested_symbol_and_custom_utc_time()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"instrument":{"ticker":"XAUUSD"},"last":2405.125,"size":5000,"time":"20260601 12:00:00.789"}""");

        var result = new GammaExchangeMessageParser().Parse(payload, "gamma", ReceivedAt);

        result.IsSuccess.Should().BeTrue();
        result.Tick!.Symbol.Should().Be("XAUUSD");
        result.Tick.Price.Should().Be(2405.125m);
        result.Tick.Volume.Should().Be(5000m);
        result.Tick.Timestamp.Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, 789, TimeSpan.Zero));
    }

    [Fact]
    public void Parser_returns_failure_instead_of_throwing_for_malformed_message()
    {
        var payload = Encoding.UTF8.GetBytes("{not-json}");

        var result = new AlphaExchangeMessageParser().Parse(payload, "alpha", ReceivedAt);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
