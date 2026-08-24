using BrokerPilot.MarketData.Application.Configuration;
using FluentAssertions;

namespace BrokerPilot.MarketData.Tests;

/// <summary>
/// Конфигурация валидируется на старте, а не в момент первого использования. Разница
/// практическая: неверный BatchSize без этой проверки уронил бы систему не при запуске,
/// а под нагрузкой, на первом полном батче, - то есть в худший из возможных моментов
/// </summary>
public sealed class MarketDataOptionsTests
{
    [Fact]
    public void Reference_configuration_is_valid()
    {
        FluentActions.Invoking(() => CreateValid().Validate()).Should().NotThrow();
    }

    [Fact]
    public void Batch_size_above_the_postgres_parameter_limit_is_rejected_at_startup()
    {
        // PostgreSQL принимает не более 65535 bind-параметров на запрос, multi-row INSERT
        // связывает 7 параметров на тик -> потолок 9362
        var options = CreateValid(o => o with { ChannelCapacity = 50_000, BatchSize = 20_000 });

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*BatchSize*");
    }

    [Fact]
    public void Batch_size_larger_than_the_queue_is_rejected()
    {
        var options = CreateValid(o => o with { ChannelCapacity = 100, BatchSize = 500 });

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Duplicate_source_names_are_rejected()
    {
        // Имя источника попадает и в ключ дедупликации, и в колонку source. Два источника
        // с одним именем схлопывали бы чужие тики в дубликаты
        var options = CreateValid(o => o with
        {
            Sources =
            [
                Source("alpha", "ws://localhost:7101/ws"),
                Source("ALPHA", "ws://localhost:7102/ws")
            ]
        });

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate exchange source name*");
    }

    [Fact]
    public void Empty_source_list_is_rejected()
    {
        var options = CreateValid(o => o with { Sources = [] });

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_fourth_exchange_is_accepted_rather_than_hardcoded_away()
    {
        // Задание описывает стенд из 2-3 бирж, но требует расширяемости. Верхняя граница,
        // зашитая в валидацию, противоречила бы этому требованию
        var options = CreateValid(o => o with
        {
            Sources =
            [
                Source("alpha", "ws://localhost:7101/ws"),
                Source("beta", "ws://localhost:7102/ws"),
                Source("gamma", "ws://localhost:7103/ws"),
                Source("delta", "ws://localhost:7104/ws")
            ]
        });

        FluentActions.Invoking(() => options.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData("http://localhost:7101/ws")]
    [InlineData("localhost:7101")]
    [InlineData("not a url")]
    public void Non_websocket_source_url_is_rejected(string url)
    {
        var options = CreateValid(o => o with { Sources = [Source("alpha", url)] });

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*WebSocket URL*");
    }

    private static ExchangeSourceOptions Source(string name, string url) => new()
    {
        Name = name,
        Format = "alpha-v1",
        Url = url,
        IdleTimeout = TimeSpan.FromSeconds(5)
    };

    private static MarketDataOptions CreateValid(Func<Snapshot, Snapshot>? mutate = null)
    {
        var snapshot = new Snapshot(
            ChannelCapacity: 20_000,
            BatchSize: 500,
            Sources:
            [
                Source("alpha", "ws://localhost:7101/ws"),
                Source("beta", "ws://localhost:7102/ws")
            ]);

        snapshot = mutate?.Invoke(snapshot) ?? snapshot;

        return new MarketDataOptions
        {
            ChannelCapacity = snapshot.ChannelCapacity,
            BatchSize = snapshot.BatchSize,
            Sources = [.. snapshot.Sources]
        };
    }

    /// <summary>
    /// Промежуточный record нужен только ради <c>with</c>-синтаксиса в тестах:
    /// <see cref="MarketDataOptions"/> — класс с init-свойствами, копировать его частично нечем
    /// </summary>
    private sealed record Snapshot(
        int ChannelCapacity,
        int BatchSize,
        IReadOnlyList<ExchangeSourceOptions> Sources);
}
