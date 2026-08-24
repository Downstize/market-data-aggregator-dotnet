using System.Diagnostics.CodeAnalysis;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Abstractions;

/// <summary>
/// Ограниченная очередь между приёмом и записью в БД.
/// Абстракция нужна не ради подмены реализации, а чтобы слой Application не зависел от
/// <c>System.Threading.Channels</c> и чтобы порядок остановки можно было тестировать на фейке
/// </summary>
public interface ITickQueue
{
    int Capacity { get; }

    int Count { get; }

    ValueTask EnqueueAsync(NormalizedTick tick, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает <see langword="null"/>, когда очередь завершена и опустошена. Именно на этом
    /// сигнале построен drain: батчер дочитывает остаток и выходит сам, без отмены
    /// </summary>
    ValueTask<NormalizedTick?> ReadAsync(CancellationToken cancellationToken);

    bool TryRead([MaybeNullWhen(false)] out NormalizedTick tick);

    /// <summary>
    /// Закрывает очередь для записи. Идемпотентен: путь остановки может быть пройден дважды
    /// </summary>
    void Complete();
}
