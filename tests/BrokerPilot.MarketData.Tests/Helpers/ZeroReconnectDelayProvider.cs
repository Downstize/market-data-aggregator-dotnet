using BrokerPilot.MarketData.Application.Abstractions;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class ZeroReconnectDelayProvider : IReconnectDelayProvider
{
    public TimeSpan GetDelay(int attemptNumber) => TimeSpan.Zero;
}
