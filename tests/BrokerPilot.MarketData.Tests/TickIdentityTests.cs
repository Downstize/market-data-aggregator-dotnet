using System.Globalization;
using BrokerPilot.MarketData.Domain;
using FluentAssertions;

namespace BrokerPilot.MarketData.Tests;

/// <summary>
/// Дедупликация в решении целиком стоит на одном утверждении: одинаковое содержимое тика
/// всегда даёт один и тот же <see cref="Guid"/>, а разное - разные. Если это утверждение
/// сломать, дедупликатор продолжит работать и тесты на конкурентность останутся зелёными -
/// он просто перестанет ловить дубликаты. Отказ был бы молчаливым, поэтому проверяется здесь
/// </summary>
public sealed class TickIdentityTests
{
    private static readonly DateTimeOffset EventTime =
        new(2026, 6, 1, 12, 0, 0, 123, TimeSpan.Zero);

    [Fact]
    public void Same_value_with_different_decimal_scale_produces_the_same_id()
    {
        // decimal хранит scale, поэтому 1.085m и 1.08500m равны по значению, но их ToString()
        // без формата даёт разные строки. Одна биржа пришлёт "1.085", другая 1.08500 - без
        // формата G29 это был бы один и тот же тик с двумя разными Id
        var first = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);
        var second = TickIdentity.Create("alpha", "EURUSD", 1.08500m, 100.000m, EventTime);

        second.Should().Be(first);
    }

    [Fact]
    public void Same_quote_from_two_exchanges_produces_two_different_ids()
    {
        // Источник входит в ключ намеренно: одинаковая котировка на двух биржах - это два
        // независимых факта, а не дубликат. Для мониторинга дилинга разница между ними и есть предмет наблюдения
        var alpha = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);
        var beta = TickIdentity.Create("beta", "EURUSD", 1.085m, 100m, EventTime);

        beta.Should().NotBe(alpha);
    }

    // Цена передаётся строкой: decimal не может быть константой атрибута, а через double
    // значение пришло бы в тест уже искажённым двоичным представлением
    [Theory]
    [InlineData("alpha", "EURUSD", "1.0851", "100")]
    [InlineData("alpha", "GBPUSD", "1.085", "100")]
    [InlineData("beta", "EURUSD", "1.085", "100")]
    [InlineData("alpha", "EURUSD", "1.085", "101")]
    public void Any_changed_field_changes_the_id(string source, string symbol, string price, string volume)
    {
        var baseline = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);

        var changed = TickIdentity.Create(
            source,
            symbol,
            decimal.Parse(price, CultureInfo.InvariantCulture),
            decimal.Parse(volume, CultureInfo.InvariantCulture),
            EventTime);

        changed.Should().NotBe(baseline);
    }

    [Fact]
    public void Timestamp_difference_below_a_millisecond_still_changes_the_id()
    {
        // Ticks (100 нс), а не отформатированное время: биржевой поток различает тики внутри
        // одной миллисекунды, и склеивать их было бы потерей данных, а не дедупликацией
        var baseline = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);

        var oneTickLater = TickIdentity.Create(
            "alpha",
            "EURUSD",
            1.085m,
            100m,
            EventTime.AddTicks(1));

        oneTickLater.Should().NotBe(baseline);
    }

    [Fact]
    public void Field_boundaries_are_not_ambiguous()
    {
        // Без разделителя между полями пары ("al", "phaEURUSD") и ("alpha", "EURUSD")
        // склеились бы в одну строку и получили один Id
        var split = TickIdentity.Create("al", "phaEURUSD", 1.085m, 100m, EventTime);
        var normal = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);

        split.Should().NotBe(normal);
    }

    [Fact]
    public void Equal_instants_in_different_offsets_produce_the_same_id()
    {
        // 12:00:00Z и 14:00:00+02:00 - один и тот же момент. Биржи присылают время в разных
        // представлениях, и Id обязан зависеть от момента, а не от того, как его записали
        var utc = TickIdentity.Create("alpha", "EURUSD", 1.085m, 100m, EventTime);
        var shifted = TickIdentity.Create(
            "alpha",
            "EURUSD",
            1.085m,
            100m,
            EventTime.ToOffset(TimeSpan.FromHours(2)));

        shifted.Should().Be(utc);
    }

    [Fact]
    public void Normalization_makes_symbol_case_and_whitespace_irrelevant()
    {
        var messy = NormalizedTick.Create("  eurusd ", 1.085m, 100m, EventTime, " ALPHA ", EventTime);
        var clean = NormalizedTick.Create("EURUSD", 1.085m, 100m, EventTime, "alpha", EventTime);

        messy.Symbol.Should().Be("EURUSD");
        messy.Source.Should().Be("alpha");
        messy.Id.Should().Be(clean.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_symbol_is_rejected_instead_of_producing_a_tick(string symbol)
    {
        FluentActions.Invoking(() =>
                NormalizedTick.Create(symbol, 1.085m, 100m, EventTime, "alpha", EventTime))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Non_positive_price_is_rejected()
    {
        FluentActions.Invoking(() =>
                NormalizedTick.Create("EURUSD", 0m, 100m, EventTime, "alpha", EventTime))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Negative_volume_is_rejected()
    {
        FluentActions.Invoking(() =>
                NormalizedTick.Create("EURUSD", 1.085m, -1m, EventTime, "alpha", EventTime))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
