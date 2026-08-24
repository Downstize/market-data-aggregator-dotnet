using System.Net.WebSockets;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

public interface IWebSocketClient : IAsyncDisposable
{
    WebSocketState State { get; }

    ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken);

    ValueTask<SocketReceiveChunk> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);

    /// <summary>
    /// Отправляет close-handshake по протоколу WebSocket. Нужен при штатной остановке, чтобы
    /// биржа увидела корректный close-фрейм, а не полуоткрытый сокет, который ей придётся
    /// отваливать по собственному таймауту
    /// </summary>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    void Abort();
}
