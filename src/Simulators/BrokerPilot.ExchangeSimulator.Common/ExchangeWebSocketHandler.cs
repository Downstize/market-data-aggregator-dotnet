using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BrokerPilot.ExchangeSimulator.Common;

public sealed class ExchangeWebSocketHandler(
    SimulationOptions options,
    IQuoteFormatter formatter,
    MarketQuoteGenerator quoteGenerator,
    ExchangeSimulationState state,
    ILogger<ExchangeWebSocketHandler> logger)
{
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                "WebSocket upgrade required",
                context.RequestAborted);

            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var connectionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var connectionId = state.Register(socket);

        logger.LogInformation(
            "{Exchange}: WebSocket client {ConnectionId} connected. Active clients: {ActiveConnections}",
            formatter.ExchangeName,
            connectionId,
            state.ActiveConnections);

        try
        {
            foreach (var replayPayload in state.GetReplayPayloads())
            {
                await SendAsync(
                        socket,
                        replayPayload,
                        connectionCancellation.Token)
                    .ConfigureAwait(false);
            }

            var sendTask = GenerateAsync(
                socket,
                connectionCancellation.Token);

            var receiveTask = ReceiveUntilCloseAsync(
                socket,
                connectionCancellation.Token);

            var completedTask = await Task.WhenAny(
                    sendTask,
                    receiveTask)
                .ConfigureAwait(false);

            WebSocketReceiveResult? closeResult = null;

            if (completedTask == receiveTask)
            {
                try
                {
                    closeResult = await receiveTask.ConfigureAwait(false);
                }
                finally
                {
                    connectionCancellation.Cancel();

                    await ObserveExpectedStopAsync(
                            sendTask,
                            socket,
                            connectionCancellation.Token)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                try
                {
                    await sendTask.ConfigureAwait(false);
                }
                finally
                {
                    connectionCancellation.Cancel();

                    closeResult = await ObserveReceiveStopAsync(
                            receiveTask,
                            socket,
                            connectionCancellation.Token)
                        .ConfigureAwait(false);
                }
            }

            if (socket.State == WebSocketState.CloseReceived)
            {
                await AcknowledgeCloseAsync(
                        socket,
                        closeResult,
                        connectionId)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            // Штатное завершение запроса или остановка приложения
        }
        catch (WebSocketException exception)
        {
            logger.LogWarning(
                exception,
                "{Exchange}: WebSocket client {ConnectionId} disconnected with a socket error",
                formatter.ExchangeName,
                connectionId);
        }
        catch (ObjectDisposedException)
        {
            logger.LogInformation(
                "{Exchange}: WebSocket client {ConnectionId} was aborted by the simulator",
                formatter.ExchangeName,
                connectionId);
        }
        finally
        {
            connectionCancellation.Cancel();

            state.Unregister(connectionId);

            logger.LogInformation(
                "{Exchange}: WebSocket client {ConnectionId} removed. Active clients: {ActiveConnections}",
                formatter.ExchangeName,
                connectionId,
                state.ActiveConnections);
        }
    }

    /// <summary>
    /// Котировки отправляются небольшими пачками по фиксированному периоду,
    /// а не по одному сообщению на Task.Delay
    /// </summary>
    private async Task GenerateAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var period = TimeSpan.FromMilliseconds(
            options.BurstIntervalMilliseconds);

        var quotesPerBurst = Math.Max(
            1,
            (int)Math.Round(
                options.TicksPerSecond * period.TotalSeconds));

        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            if (state.Paused)
            {
                continue;
            }

            for (var index = 0; index < quotesPerBurst; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                var quote = quoteGenerator.Next();
                var payload = formatter.Serialize(quote);
                var messageNumber = state.IncrementGeneratedMessages();

                state.Remember(payload);

                await SendAsync(
                        socket,
                        payload,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (state.ShouldDuplicate(messageNumber))
                {
                    await SendAsync(
                            socket,
                            payload,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Симулятор не ожидает бизнес-сообщений от агрегатора,
    /// но обязан читать входящую сторону WebSocket, чтобы
    /// корректно получить Close frame
    /// </summary>
    private static async Task<WebSocketReceiveResult?> ReceiveUntilCloseAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[256];

        while (!cancellationToken.IsCancellationRequested &&
               socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result;
            }

            // Клиентских бизнес-сообщений симулятор не ожидает.
            // Любые входящие data frames просто дочитываются и игнорируются
        }

        return null;
    }

    private async Task AcknowledgeCloseAsync(
        WebSocket socket,
        WebSocketReceiveResult? closeResult,
        Guid connectionId)
    {
        using var closeTimeout =
            new CancellationTokenSource(CloseHandshakeTimeout);

        try
        {
            logger.LogInformation(
                "{Exchange}: WebSocket client {ConnectionId} requested graceful close",
                formatter.ExchangeName,
                connectionId);

            await socket.CloseOutputAsync(
                    closeResult?.CloseStatus
                    ?? WebSocketCloseStatus.NormalClosure,
                    closeResult?.CloseStatusDescription,
                    closeTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug(
                "{Exchange}: close handshake with client {ConnectionId} timed out",
                formatter.ExchangeName,
                connectionId);
        }
        catch (WebSocketException exception)
        {
            logger.LogDebug(
                exception,
                "{Exchange}: failed to finish close handshake with client {ConnectionId}",
                formatter.ExchangeName,
                connectionId);
        }
    }

    private static async Task ObserveExpectedStopAsync(
        Task task,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Парная задача остановлена намеренно
        }
        catch (WebSocketException)
            when (socket.State != WebSocketState.Open)
        {
            // Соединение уже закрывается
        }
        catch (ObjectDisposedException)
            when (socket.State != WebSocketState.Open)
        {
            // Сокет уже закрыт/освобождён
        }
    }

    private static async Task<WebSocketReceiveResult?> ObserveReceiveStopAsync(
        Task<WebSocketReceiveResult?> task,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (WebSocketException)
            when (socket.State != WebSocketState.Open)
        {
            return null;
        }
        catch (ObjectDisposedException)
            when (socket.State != WebSocketState.Open)
        {
            return null;
        }
    }

    private static Task SendAsync(
        WebSocket socket,
        byte[] payload,
        CancellationToken cancellationToken) =>
        socket.SendAsync(
                payload.AsMemory(),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            .AsTask();
}
