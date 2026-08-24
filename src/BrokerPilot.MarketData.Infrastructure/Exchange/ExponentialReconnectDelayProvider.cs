using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;

namespace BrokerPilot.MarketData.Infrastructure.Exchange;

public sealed class ExponentialReconnectDelayProvider(MarketDataOptions options) : IReconnectDelayProvider
{
    private const double JitterRatio = 0.20;

    public TimeSpan GetDelay(int attemptNumber)
    {
        var exponent = Math.Clamp(attemptNumber - 1, 0, 20);
        var uncappedMilliseconds = options.ReconnectInitialDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedMilliseconds = Math.Min(uncappedMilliseconds, options.ReconnectMaxDelay.TotalMilliseconds);

        var jitterMultiplier = 1 + ((Random.Shared.NextDouble() * 2 - 1) * JitterRatio);
        var withJitter = Math.Max(0, cappedMilliseconds * jitterMultiplier);

        return TimeSpan.FromMilliseconds(Math.Min(withJitter, options.ReconnectMaxDelay.TotalMilliseconds));
    }
}
