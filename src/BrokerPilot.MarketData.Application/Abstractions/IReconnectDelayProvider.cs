namespace BrokerPilot.MarketData.Application.Abstractions;

public interface IReconnectDelayProvider
{
    /// <summary>
    /// Задержка перед попыткой переподключения. <paramref name="attemptNumber"/> нумеруется
    /// с единицы: 1 - первая попытка после обрыва. Контракт намеренно совпадает с
    /// <see cref="IDatabaseRetryDelayProvider"/>, чтобы два похожих backoff'а в решении не
    /// расходились в том, откуда начинается счёт
    /// </summary>
    TimeSpan GetDelay(int attemptNumber);
}
