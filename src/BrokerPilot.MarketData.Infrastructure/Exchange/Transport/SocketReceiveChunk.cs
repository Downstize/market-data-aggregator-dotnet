using System.Net.WebSockets;

namespace BrokerPilot.MarketData.Infrastructure.Exchange.Transport;

/// <summary>
/// Одна порция данных, снятая с сокета. readonly record struct, а не class: на 750 тиков/сек
/// это 750 лишних аллокаций в секунду, которые не нужны для трёх полей
/// </summary>
public readonly record struct SocketReceiveChunk(
    int Count,
    bool EndOfMessage,
    WebSocketMessageType MessageType);
