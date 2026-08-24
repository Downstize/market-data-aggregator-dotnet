using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Tests.Helpers;

/// <summary>
/// Кладёт в очередь фиксированное число тиков и дальше ведёт себя как здоровый простаивающий
/// фид: ждёт отмены и завершается штатно — ровно так же, как настоящий WebSocket-фид.
/// </summary>
internal sealed class ScriptedExchangeFeed(string name, int tickCount, ITickQueue queue, int offsetSeed)
    : IExchangeFeed
{
    private readonly TaskCompletionSource _allEnqueued =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => name;

    public Task AllEnqueued => _allEnqueued.Task;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 0; index < tickCount; index++)
            {
                var tick = TestTicks.Create(offsetSeed + index, name);
                await queue.EnqueueAsync(tick, cancellationToken).ConfigureAwait(false);
            }

            _allEnqueued.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _allEnqueued.TrySetResult();
        }
    }
}

internal sealed class ScriptedExchangeFeedFactory(ITickQueue queue, int ticksPerFeed) : IExchangeFeedFactory
{
    private readonly List<ScriptedExchangeFeed> _feeds = [];

    public IReadOnlyList<ScriptedExchangeFeed> Feeds => _feeds.ToArray();

    public IExchangeFeed Create(ExchangeSourceOptions source)
    {
        var feed = new ScriptedExchangeFeed(source.Name, ticksPerFeed, queue, _feeds.Count * 10_000);
        _feeds.Add(feed);
        return feed;
    }
}
