using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Abstractions;

/// <summary>
/// Результат разбора одного сообщения биржи. Битое сообщение - ожидаемое событие на живом
/// фиде, а не исключительная ситуация, поэтому ошибка возвращается значением: исключение
/// на каждый мусорный тик стоило бы дороже самого разбора и засоряло бы стек вызовов
/// </summary>
public sealed record TickParseResult(NormalizedTick? Tick, string? Error)
{
    public bool IsSuccess => Tick is not null;

    public static TickParseResult Success(NormalizedTick tick) => new(tick, null);

    public static TickParseResult Failure(string error) => new(null, error);
}
