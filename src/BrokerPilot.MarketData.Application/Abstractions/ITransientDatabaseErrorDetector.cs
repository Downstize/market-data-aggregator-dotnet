namespace BrokerPilot.MarketData.Application.Abstractions;

/// <summary>
/// Отличает временный сбой БД от постоянной ошибки (нарушение constraint, отсутствующая таблица, значение не влезает в тип).
/// Ретрай постоянной ошибки только тратит бюджет попыток и откладывает запись в dead-letter
/// </summary>
public interface ITransientDatabaseErrorDetector
{
    bool IsTransient(Exception exception);
}
