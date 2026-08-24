using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;

namespace BrokerPilot.MarketData.Infrastructure.Persistence;

public sealed class ExponentialDatabaseRetryDelayProvider(MarketDataOptions options) : IDatabaseRetryDelayProvider
{
    public TimeSpan GetDelay(int attemptNumber)
    {
        var exponent = Math.Clamp(attemptNumber - 1, 0, 20);
        var milliseconds = options.DatabaseRetryInitialDelay.TotalMilliseconds * Math.Pow(2, exponent);

        // Jitter здесь не нужен: писатель в системе один, синхронизироваться не с кем
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, options.DatabaseRetryMaxDelay.TotalMilliseconds));
    }
}
