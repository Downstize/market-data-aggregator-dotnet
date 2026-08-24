using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class CapturingTickQueue(int cancelAfter, CancellationTokenSource cancellation) : ITickQueue
{
    private readonly ConcurrentQueue<NormalizedTick> _items = new();
    private int _count;

    public int Capacity => int.MaxValue;

    public int Count => _items.Count;

    public IReadOnlyCollection<NormalizedTick> Items => _items.ToArray();

    public ValueTask EnqueueAsync(NormalizedTick tick, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.Enqueue(tick);

        // Interlocked, а не _items.Count: несколько фидов пишут конкурентно, и условие остановки
        // должно сработать ровно один раз — на том тике, который пересёк порог.
        if (Interlocked.Increment(ref _count) == cancelAfter)
        {
            cancellation.Cancel();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<NormalizedTick?> ReadAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool TryRead([MaybeNullWhen(false)] out NormalizedTick tick) =>
        throw new NotSupportedException();

    public void Complete()
    {
    }
}
