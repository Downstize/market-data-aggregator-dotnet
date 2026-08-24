using System.Net.WebSockets;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Infrastructure.Exchange.Transport;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.MarketData.Infrastructure.Exchange;

public sealed class WebSocketExchangeFeed : IExchangeFeed
{
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    private readonly ExchangeSourceOptions _source;
    private readonly Uri _uri;
    private readonly IExchangeMessageParser _parser;
    private readonly IWebSocketClientFactory _clientFactory;
    private readonly ITickDeduplicator _deduplicator;
    private readonly ITickQueue _queue;
    private readonly IAggregatorMetrics _metrics;
    private readonly IReconnectDelayProvider _reconnectDelayProvider;
    private readonly MarketDataOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebSocketExchangeFeed> _logger;

    public WebSocketExchangeFeed(
        ExchangeSourceOptions source,
        IExchangeMessageParser parser,
        IWebSocketClientFactory clientFactory,
        ITickDeduplicator deduplicator,
        ITickQueue queue,
        IAggregatorMetrics metrics,
        IReconnectDelayProvider reconnectDelayProvider,
        MarketDataOptions options,
        TimeProvider timeProvider,
        ILogger<WebSocketExchangeFeed> logger)
    {
        _source = source;
        _uri = new Uri(source.Url, UriKind.Absolute);
        _parser = parser;
        _clientFactory = clientFactory;
        _deduplicator = deduplicator;
        _queue = queue;
        _metrics = metrics;
        _reconnectDelayProvider = reconnectDelayProvider;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Name => _source.Name;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        var receiveBuffer = new byte[_options.ReceiveBufferSize];
        using var messageBuffer = new MemoryStream(_options.ReceiveBufferSize);

        // Переиспользуется на каждом ReceiveAsync, чтобы idle-watchdog не создавал linked-
        // источник отмены и регистрацию таймера на каждое сообщение
        using var idleTimeout = new ReusableLinkedTimeout(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var client = _clientFactory.Create();
            long? connectedAtTimestamp = null;

            try
            {
                await client.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
                connectedAtTimestamp = _timeProvider.GetTimestamp();
                _metrics.SourceConnected(Name);
                _logger.LogInformation(
                    "Connected to exchange {Exchange} at {Url}",
                    Name,
                    _uri);

                while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
                {
                    var hasMessage = await ReceiveAndProcessMessageAsync(
                            client,
                            receiveBuffer,
                            messageBuffer,
                            idleTimeout,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!hasMessage)
                    {
                        _logger.LogWarning("Exchange {Exchange} closed the WebSocket connection", Name);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CloseGracefullyAsync(client).ConfigureAwait(false);
                break;
            }
            catch (IdleConnectionTimeoutException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Exchange {Exchange} became idle for more than {IdleTimeout}; reconnecting",
                    Name,
                    _source.IdleTimeout);
            }
            catch (WebSocketException exception)
            {
                _logger.LogWarning(
                    exception,
                    "WebSocket failure for exchange {Exchange}; reconnecting",
                    Name);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unexpected exchange feed failure for {Exchange}; reconnecting instead of terminating other feeds",
                    Name);
            }
            finally
            {
                _metrics.SourceDisconnected(Name);

                try
                {
                    client.Abort();
                }
                catch (ObjectDisposedException)
                {
                    // Освобождение объекта происходит сразу следом, больше ничего не требуется
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            consecutiveFailures = NextFailureCount(consecutiveFailures, connectedAtTimestamp);

            _metrics.ReconnectScheduled(Name);
            var delay = _reconnectDelayProvider.GetDelay(consecutiveFailures);

            _logger.LogInformation(
                "Reconnect for exchange {Exchange} is scheduled in {DelayMs} ms (consecutive failure #{FailureNumber})",
                Name,
                delay.TotalMilliseconds,
                consecutiveFailures);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Exchange feed {Exchange} stopped.", Name);
    }

    /// <summary>
    /// Счётчик backoff сбрасывается только после того, как соединение прожило
    /// <see cref="MarketDataOptions.ReconnectStabilityThreshold"/>. Сброс по первому принятому
    /// сообщению позволил бы флапающей бирже (принять подключение, отдать один тик, оборваться)
    /// вечно держать задержку на начальном значении - то есть плотный цикл переподключений
    /// в костюме экспоненциального backoff
    /// </summary>
    private int NextFailureCount(int consecutiveFailures, long? connectedAtTimestamp)
    {
        if (connectedAtTimestamp is { } timestamp &&
            _timeProvider.GetElapsedTime(timestamp) >= _options.ReconnectStabilityThreshold)
        {
            return 1;
        }

        return Math.Min(consecutiveFailures + 1, 31);
    }

    private async Task CloseGracefullyAsync(IWebSocketClient client)
    {
        if (client.State != WebSocketState.Open)
        {
            return;
        }

        // Основной токен фида здесь уже отменён, поэтому handshake'у нужен собственный
        using var closeTimeout = new CancellationTokenSource(CloseHandshakeTimeout);

        try
        {
            await client.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Close handshake with exchange {Exchange} did not complete; the socket will be aborted instead",
                Name);
        }
    }

    private async Task<bool> ReceiveAndProcessMessageAsync(
        IWebSocketClient client,
        byte[] receiveBuffer,
        MemoryStream messageBuffer,
        ReusableLinkedTimeout idleTimeout,
        CancellationToken cancellationToken)
    {
        messageBuffer.SetLength(0);
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var idleToken = idleTimeout.Start(_source.IdleTimeout);

            SocketReceiveChunk chunk;
            try
            {
                chunk = await client.ReceiveAsync(receiveBuffer, idleToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IdleConnectionTimeoutException(Name, _source.IdleTimeout);
            }

            if (chunk.MessageType == WebSocketMessageType.Close)
            {
                return false;
            }

            messageType ??= chunk.MessageType;

            if (messageBuffer.Length + chunk.Count > _options.MaxMessageBytes)
            {
                throw new InvalidDataException(
                    $"Message from exchange '{Name}' exceeds {_options.MaxMessageBytes} bytes");
            }

            if (chunk.Count > 0)
            {
                messageBuffer.Write(receiveBuffer, 0, chunk.Count);
            }

            if (chunk.EndOfMessage)
            {
                break;
            }
        }

        _metrics.RawTickReceived();

        if (messageType != WebSocketMessageType.Text)
        {
            _metrics.TickInvalid();
            _logger.LogWarning(
                "Exchange {Exchange} sent a non-text WebSocket message; it was ignored",
                Name);
            return true;
        }

        var receivedAt = _timeProvider.GetUtcNow();
        var payload = messageBuffer.GetBuffer().AsMemory(0, checked((int)messageBuffer.Length));
        var parseResult = _parser.Parse(payload, Name, receivedAt);

        if (!parseResult.IsSuccess)
        {
            _metrics.TickInvalid();
            _logger.LogWarning(
                "Exchange {Exchange} sent an invalid tick: {Reason}",
                Name,
                parseResult.Error);
            return true;
        }

        var tick = parseResult.Tick!;
        if (!_deduplicator.TryAccept(tick.Id))
        {
            _metrics.TickDuplicate();
            return true;
        }

        try
        {
            await _queue.EnqueueAsync(tick, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Единственный способ сюда попасть: очередь заполнена (backpressure), фид ждёт места,
            // и в этот момент приходит остановка. Тик уже принят дедупликатором, поэтому повторная
            // доставка после переподключения будет отброшена как дубликат - вернуть его неоткуда.
            // Считаем явно: задание запрещает молчаливые потери, а не потери как таковые
            _metrics.TicksDropped(1);
            _logger.LogWarning(
                "Tick from exchange {Exchange} was dropped: shutdown arrived while the queue was full. " +
                "It is counted in TicksDropped",
                Name);
            throw;
        }

        _metrics.TickAccepted();

        return true;
    }

    private sealed class IdleConnectionTimeoutException(string exchange, TimeSpan timeout)
        : TimeoutException($"Exchange '{exchange}' produced no data for {timeout}");

    /// <summary>
    /// Linked-источник отмены, который перевзводится, а не пересоздаётся. TryReset возвращает
    /// false только если источник уже был отменён - а это ровно тот случай, когда новый
    /// экземпляр действительно необходим
    /// </summary>
    private sealed class ReusableLinkedTimeout(CancellationToken parent) : IDisposable
    {
        private CancellationTokenSource? _source;

        public CancellationToken Start(TimeSpan timeout)
        {
            if (_source is null || !_source.TryReset())
            {
                _source?.Dispose();
                _source = CancellationTokenSource.CreateLinkedTokenSource(parent);
            }

            _source.CancelAfter(timeout);
            return _source.Token;
        }

        public void Dispose() => _source?.Dispose();
    }
}
