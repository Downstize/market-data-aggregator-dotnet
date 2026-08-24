using BrokerPilot.MarketData.Application.Abstractions;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class FakeTransientErrorDetector(bool isTransient) : ITransientDatabaseErrorDetector
{
    public static FakeTransientErrorDetector Transient { get; } = new(true);

    public static FakeTransientErrorDetector Permanent { get; } = new(false);

    public bool IsTransient(Exception exception) => isTransient;
}
