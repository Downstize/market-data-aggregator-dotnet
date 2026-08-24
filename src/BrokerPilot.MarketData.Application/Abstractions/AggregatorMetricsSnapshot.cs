namespace BrokerPilot.MarketData.Application.Abstractions;

/// <summary>
/// Потокобезопасный снимок счётчиков. Каждое значение читается атомарно, но весь
/// набор не является транзакционным: между чтением двух полей рабочие потоки могут
/// успеть обновить метрики. Для мониторинга это ожидаемо и не влияет на корректность pipeline
/// </summary>
public sealed record AggregatorMetricsSnapshot(
    long RawTicksReceived,
    long TicksAccepted,
    long DuplicateTicks,
    long InvalidTicks,
    long TicksWritten,
    long DatabaseConflicts,
    long DatabaseWriteAttemptFailures,
    long TicksDeadLettered,
    long TicksDropped,
    long ReconnectsScheduled,
    IReadOnlyDictionary<string, bool> SourceConnections);
