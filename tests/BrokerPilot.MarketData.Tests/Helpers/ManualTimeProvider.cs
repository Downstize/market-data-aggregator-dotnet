namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
        _timestamp = checked(_timestamp + duration.Ticks);
    }

    public void AdjustUtcNow(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
