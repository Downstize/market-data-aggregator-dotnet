using Microsoft.Extensions.Hosting;

namespace BrokerPilot.MarketData.Tests.Helpers;

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    private int _stopApplicationCalls;

    public CancellationToken ApplicationStarted => _started.Token;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => _stopped.Token;

    public int StopApplicationCalls => Volatile.Read(ref _stopApplicationCalls);

    public void StopApplication()
    {
        Interlocked.Increment(ref _stopApplicationCalls);
        _stopping.Cancel();
    }

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }
}
