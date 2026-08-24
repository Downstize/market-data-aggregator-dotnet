namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IAggregatorMetrics
{
    void RawTickReceived();

    void TickAccepted();

    void TickDuplicate();

    void TickInvalid();

    void TicksWritten(int count);

    void DatabaseConflict(int count);

    /// <summary>
    /// Одна неудачная ПОПЫТКА, а не один неудачный батч: один батч может увеличить счётчик
    /// несколько раз, прежде чем запись удастся или батч уедет в dead-letter
    /// </summary>
    void DatabaseWriteAttemptFailed();

    void TicksDeadLettered(int count);

    void TicksDropped(int count);

    void SourceConnected(string source);

    void SourceDisconnected(string source);

    void ReconnectScheduled(string source);

    AggregatorMetricsSnapshot Snapshot();
}
