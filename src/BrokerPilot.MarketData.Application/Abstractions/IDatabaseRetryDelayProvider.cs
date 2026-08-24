namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IDatabaseRetryDelayProvider
{
    /// <summary>
    /// Задержка перед повтором записи в БД. <paramref name="attemptNumber"/> нумеруется
    /// с единицы: 1 - первый повтор после неудачной первой попытки
    /// </summary>
    TimeSpan GetDelay(int attemptNumber);
}
