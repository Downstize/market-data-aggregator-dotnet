using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Abstractions;

/// <summary>
/// Отделён от <see cref="ITickRepository"/>, который умеет только
/// "выполнить одну вставку" и ничего не знает про то, что делать при её падении
/// </summary>
public interface ITickBatchWriter
{
    /// <summary>
    /// Забирает батч под свою ответственность: после возврата из метода - успешного или через
    /// исключение - каждый тик обязан иметь явный исход. Молча выбросить батч реализация права не имеет
    /// </summary>
    Task WriteAsync(
        IReadOnlyList<NormalizedTick> ticks,
        CancellationToken cancellationToken);
}
