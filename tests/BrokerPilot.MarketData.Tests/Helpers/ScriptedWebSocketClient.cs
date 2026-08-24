using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class ScriptedWebSocketClient(IEnumerable<ScriptedFrame> frames) : IWebSocketClient
{
    private readonly ConcurrentQueue<ScriptedFrame> _frames = new(frames);

    public WebSocketState State { get; private set; } = WebSocketState.None;

    public int ConnectCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCalls++;
        State = WebSocketState.Open;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<SocketReceiveChunk> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (_frames.TryDequeue(out var frame))
        {
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                State = WebSocketState.CloseReceived;
                return new SocketReceiveChunk(0, true, WebSocketMessageType.Close);
            }

            if (frame.Payload.Length > buffer.Length)
            {
                throw new InvalidOperationException("Test frame is larger than receive buffer.");
            }

            frame.Payload.AsMemory().CopyTo(buffer);
            return new SocketReceiveChunk(frame.Payload.Length, true, frame.MessageType);
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        CloseCalls++;
        State = WebSocketState.Closed;
        return ValueTask.CompletedTask;
    }

    public void Abort() => State = WebSocketState.Aborted;

    public ValueTask DisposeAsync()
    {
        State = WebSocketState.Closed;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Не отдаёт ни одного фрейма: сокет остаётся открытым и молчит. Именно так выглядит для
/// агрегатора «зависшее, но формально живое» соединение.
/// </summary>
internal sealed class SilentWebSocketClient : IWebSocketClient
{
    public WebSocketState State { get; private set; } = WebSocketState.None;

    public ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = WebSocketState.Open;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<SocketReceiveChunk> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        State = WebSocketState.Closed;
        return ValueTask.CompletedTask;
    }

    public void Abort() => State = WebSocketState.Aborted;

    public ValueTask DisposeAsync()
    {
        State = WebSocketState.Closed;
        return ValueTask.CompletedTask;
    }
}

internal sealed record ScriptedFrame(byte[] Payload, WebSocketMessageType MessageType)
{
    public static ScriptedFrame Text(string payload) =>
        new(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text);

    public static ScriptedFrame Close() =>
        new([], WebSocketMessageType.Close);
}

internal sealed class ScriptedWebSocketClientFactory(IEnumerable<IWebSocketClient> clients) : IWebSocketClientFactory
{
    private readonly ConcurrentQueue<IWebSocketClient> _clients = new(clients);
    private int _createdCount;

    public int CreatedCount => Volatile.Read(ref _createdCount);

    public IWebSocketClient Create()
    {
        Interlocked.Increment(ref _createdCount);

        if (_clients.TryDequeue(out var client))
        {
            return client;
        }

        throw new InvalidOperationException("No scripted WebSocket client remains.");
    }
}
