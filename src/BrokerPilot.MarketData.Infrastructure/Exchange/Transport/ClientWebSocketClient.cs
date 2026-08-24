using System.Net.WebSockets;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

public sealed class ClientWebSocketClient : IWebSocketClient
{
    private readonly ClientWebSocket _socket = new();

    public WebSocketState State => _socket.State;

    public async ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

    public async ValueTask<SocketReceiveChunk> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new SocketReceiveChunk(result.Count, result.EndOfMessage, result.MessageType);
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken) =>
        await _socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Aggregator is shutting down",
                cancellationToken)
            .ConfigureAwait(false);

    public void Abort() => _socket.Abort();

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
