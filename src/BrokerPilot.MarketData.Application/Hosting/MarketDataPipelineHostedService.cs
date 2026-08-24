using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Application.Hosting;

/// <summary>
/// Владеет временем жизни всех долгоживущих задач пайплайна
/// </summary>
public sealed class MarketDataPipelineHostedService : IHostedService, IDisposable
{
    private readonly MarketDataOptions _options;
    private readonly IExchangeFeedFactory _feedFactory;
    private readonly ITickQueue _queue;
    private readonly ITickBatchConsumer _batchConsumer;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<MarketDataPipelineHostedService> _logger;

    private readonly CancellationTokenSource _ingestionStop = new();
    private readonly CancellationTokenSource _forceStop = new();
    private readonly List<Task> _observerTasks = [];
    private IReadOnlyList<(IExchangeFeed Feed, Task Task)> _feedTasks = [];
    private Task? _writerTask;
    private int _stopping;
    private int _disposed;
    private bool _started;

    public MarketDataPipelineHostedService(
        MarketDataOptions options,
        IExchangeFeedFactory feedFactory,
        ITickQueue queue,
        ITickBatchConsumer batchConsumer,
        IHostApplicationLifetime applicationLifetime,
        ILogger<MarketDataPipelineHostedService> logger)
    {
        _options = options;
        _feedFactory = feedFactory;
        _queue = queue;
        _batchConsumer = batchConsumer;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_started)
        {
            throw new InvalidOperationException("Market data pipeline has already been started");
        }

        _started = true;

        var feeds = _options.Sources
            .Select(_feedFactory.Create)
            .ToArray();

        // У писателя собственный токен: он обязан пережить отмену, которая останавливает приём.
        // Иначе drain оборвался бы ровно в тот момент, когда он и нужен
        _writerTask = _batchConsumer.RunAsync(_forceStop.Token);
        _observerTasks.Add(ObserveWriterAsync(_writerTask));

        _feedTasks = feeds
            .Select(feed => (Feed: feed, Task: feed.RunAsync(_ingestionStop.Token)))
            .ToArray();

        foreach (var (feed, task) in _feedTasks)
        {
            _observerTasks.Add(ObserveFeedAsync(feed, task));
        }

        _logger.LogInformation(
            "Market data pipeline started with {SourceCount} exchange feeds, queue capacity {QueueCapacity}, batch size {BatchSize}",
            feeds.Length,
            _queue.Capacity,
            _options.BatchSize);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started || Interlocked.Exchange(ref _stopping, 1) == 1)
        {
            return;
        }

        _logger.LogInformation(
            "Graceful shutdown started: stopping exchange ingestion before draining {QueueDepth} queued ticks",
            _queue.Count);

        Cancel(_ingestionStop);

        try
        {
            await Task.WhenAll(_feedTasks.Select(item => item.Task))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Host shutdown token fired while waiting for exchange feeds to stop");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "One or more exchange feeds faulted while stopping");
        }
        finally
        {
            _queue.Complete();
        }

        if (_writerTask is not null)
        {
            using var drainTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainTimeout.CancelAfter(_options.DrainTimeout);

            try
            {
                await _writerTask.WaitAsync(drainTimeout.Token).ConfigureAwait(false);
                _logger.LogInformation(
                    "Queue drain completed successfully. Remaining queue depth: {QueueDepth}",
                    _queue.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogCritical(
                    "Graceful drain did not finish within {DrainTimeout}. Forcing stop with {QueueDepth} ticks still queued; " +
                    "the batch consumer will account for them in TicksDropped",
                    _options.DrainTimeout,
                    _queue.Count);
                Cancel(_forceStop);
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "Batch consumer faulted during graceful shutdown");
                Cancel(_forceStop);
            }
        }

        Cancel(_forceStop);

        try
        {
            await Task.WhenAll(_observerTasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Pipeline observer tasks did not finish before shutdown completed");
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо при форсированной остановке
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _ingestionStop.Dispose();
        _forceStop.Dispose();
    }

    /// <summary>
    /// Задача-наблюдатель, не уложившаяся в двухсекундное окно ожидания, может обратиться к
    /// источнику отмены, который Dispose уже освободил. Cancel на освобождённом источнике
    /// бросает исключение, и оно осело бы в задаче, за которой никто не следит
    /// </summary>
    private void Cancel(CancellationTokenSource source)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Остановка уже завершилась и освободила источник. Отменять больше нечего
        }
    }

    private async Task ObserveWriterAsync(Task writerTask)
    {
        try
        {
            await writerTask.ConfigureAwait(false);

            if (Volatile.Read(ref _stopping) == 0)
            {
                _logger.LogCritical("Batch consumer stopped unexpectedly while the application was still running");
                Cancel(_ingestionStop);
                _applicationLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (_forceStop.IsCancellationRequested)
        {
            // Ожидаемо при форсированной остановке
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Batch consumer faulted. Stopping the application to avoid silent data loss");
            Cancel(_ingestionStop);
            _applicationLifetime.StopApplication();
        }
    }

    private async Task ObserveFeedAsync(IExchangeFeed feed, Task feedTask)
    {
        try
        {
            await feedTask.ConfigureAwait(false);

            if (Volatile.Read(ref _stopping) == 0)
            {
                _logger.LogError(
                    "Exchange feed {Exchange} stopped unexpectedly. The feed implementation should reconnect internally",
                    feed.Name);
                _applicationLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (_ingestionStop.IsCancellationRequested)
        {
            // Ожидаемо при штатной остановке
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Exchange feed {Exchange} escaped its reconnect loop. Stopping the application instead of silently losing that source",
                feed.Name);
            _applicationLifetime.StopApplication();
        }
    }
}
