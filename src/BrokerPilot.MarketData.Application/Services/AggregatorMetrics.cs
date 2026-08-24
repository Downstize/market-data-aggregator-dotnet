using System.Collections.Concurrent;
using BrokerPilot.MarketData.Application.Abstractions;

namespace BrokerPilot.MarketData.Application.Services;

public sealed class AggregatorMetrics : IAggregatorMetrics
{
    private readonly ConcurrentDictionary<string, bool> _sourceConnections =
        new(StringComparer.OrdinalIgnoreCase);

    private long _rawTicksReceived;
    private long _ticksAccepted;
    private long _duplicateTicks;
    private long _invalidTicks;
    private long _ticksWritten;
    private long _databaseConflicts;
    private long _databaseWriteAttemptFailures;
    private long _ticksDeadLettered;
    private long _ticksDropped;
    private long _reconnectsScheduled;

    public void RawTickReceived() => Interlocked.Increment(ref _rawTicksReceived);

    public void TickAccepted() => Interlocked.Increment(ref _ticksAccepted);

    public void TickDuplicate() => Interlocked.Increment(ref _duplicateTicks);

    public void TickInvalid() => Interlocked.Increment(ref _invalidTicks);

    public void TicksWritten(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _ticksWritten, count);
        }
    }

    public void DatabaseConflict(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _databaseConflicts, count);
        }
    }

    public void DatabaseWriteAttemptFailed() => Interlocked.Increment(ref _databaseWriteAttemptFailures);

    public void TicksDeadLettered(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _ticksDeadLettered, count);
        }
    }

    public void TicksDropped(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _ticksDropped, count);
        }
    }

    public void SourceConnected(string source) => _sourceConnections[source] = true;

    public void SourceDisconnected(string source) => _sourceConnections[source] = false;

    public void ReconnectScheduled(string source)
    {
        _sourceConnections[source] = false;
        Interlocked.Increment(ref _reconnectsScheduled);
    }

    public AggregatorMetricsSnapshot Snapshot() => new(
        Interlocked.Read(ref _rawTicksReceived),
        Interlocked.Read(ref _ticksAccepted),
        Interlocked.Read(ref _duplicateTicks),
        Interlocked.Read(ref _invalidTicks),
        Interlocked.Read(ref _ticksWritten),
        Interlocked.Read(ref _databaseConflicts),
        Interlocked.Read(ref _databaseWriteAttemptFailures),
        Interlocked.Read(ref _ticksDeadLettered),
        Interlocked.Read(ref _ticksDropped),
        Interlocked.Read(ref _reconnectsScheduled),
        _sourceConnections.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase));
}
