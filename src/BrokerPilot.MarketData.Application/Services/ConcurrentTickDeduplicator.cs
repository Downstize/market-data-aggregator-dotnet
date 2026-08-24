using System.Collections.Concurrent;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;

namespace BrokerPilot.MarketData.Application.Services;

/// <summary>
/// Потокобезопасный дедупликатор с ограниченным окном жизни записей.
/// <para>
/// Для атомарных операций над одним ключом используется <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Здесь нет небезопасной последовательности "ContainsKey, затем Add": решение о принятии тика
/// делается через TryAdd/TryUpdate, поэтому два конкурентных вызова не смогут одновременно
/// принять один и тот же живой ключ
/// </para>
/// </summary>
public sealed class ConcurrentTickDeduplicator : ITickDeduplicator
{
    private readonly ConcurrentDictionary<Guid, long> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly int _cleanupEvery;
    private long _operationCount;
    private int _cleanupInProgress;

    public ConcurrentTickDeduplicator(MarketDataOptions options, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _window = options.DeduplicationWindow;
        _cleanupEvery = options.DeduplicationCleanupEvery;
    }

    public int ApproximateEntryCount => _entries.Count;

    public bool TryAccept(Guid tickId)
    {
        // Для TTL нужен монотонный timestamp, а не UTC-время. Системные часы могут быть
        // скорректированы NTP/администратором вперёд или назад; это не должно внезапно сокращать
        // или растягивать окно дедупликации
        var nowTimestamp = _timeProvider.GetTimestamp();

        while (true)
        {
            // Основной путь: ключа раньше не было. TryAdd атомарно добавляет конкретный ключ,
            // поэтому true получит только один из конкурирующих вызовов для этого tickId
            if (_entries.TryAdd(tickId, nowTimestamp))
            {
                CleanupIfNeeded(nowTimestamp);
                return true;
            }

            // Между неудачным TryAdd и чтением значение мог удалить cleanup
            if (!_entries.TryGetValue(tickId, out var acceptedAtTimestamp))
            {
                continue;
            }

            // Запись ещё находится внутри окна - это дубликат
            if (_timeProvider.GetElapsedTime(acceptedAtTimestamp, nowTimestamp) < _window)
            {
                CleanupIfNeeded(nowTimestamp);
                return false;
            }

            // Окно истекло. TryUpdate работает как compare-and-swap для пары key/value:
            // обновляем timestamp только если другой поток не изменил запись после нашего чтения
            if (_entries.TryUpdate(tickId, nowTimestamp, acceptedAtTimestamp))
            {
                CleanupIfNeeded(nowTimestamp);
                return true;
            }
        }
    }

    /// <summary>
    /// Амортизированное вытеснение протухших записей вместо отдельного фонового worker-а.
    /// Частота очистки пропорциональна входящему трафику, а single-flight guard не позволяет
    /// двум дорогим проходам по словарю выполняться одновременно
    /// </summary>
    private void CleanupIfNeeded(long nowTimestamp)
    {
        var operation = Interlocked.Increment(ref _operationCount);
        if (operation % _cleanupEvery != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _cleanupInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // Перечисление ConcurrentDictionary безопасно при конкурентных изменениях. Если
            // конкретную запись не удалось убрать сейчас, она будет рассмотрена на следующем проходе
            foreach (var entry in _entries)
            {
                if (_timeProvider.GetElapsedTime(entry.Value, nowTimestamp) < _window)
                {
                    continue;
                }

                // Перегрузка с KeyValuePair удаляет запись только если значение всё ещё совпадает.
                // Если другой поток уже продлил окно через TryUpdate, свежая запись не будет удалена
                _entries.TryRemove(entry);
            }
        }
        finally
        {
            Volatile.Write(ref _cleanupInProgress, 0);
        }
    }
}
