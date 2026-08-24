using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace BrokerPilot.ExchangeSimulator.Common;

public sealed class ExchangeSimulationState
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();
    private readonly object _recentGate = new();
    private readonly Queue<byte[]> _recentPayloads = new();
    private readonly SimulationOptions _options;
    private int _duplicatesEnabled;
    private int _paused;
    private long _generatedMessages;

    public ExchangeSimulationState(SimulationOptions options)
    {
        _options = options;
        _duplicatesEnabled = options.DuplicatesEnabledAtStartup ? 1 : 0;
    }

    public int ActiveConnections => _connections.Count;

    public bool DuplicatesEnabled => Volatile.Read(ref _duplicatesEnabled) == 1;

    public bool Paused => Volatile.Read(ref _paused) == 1;

    public long GeneratedMessages => Interlocked.Read(ref _generatedMessages);

    public Guid Register(WebSocket socket)
    {
        var id = Guid.NewGuid();
        if (!_connections.TryAdd(id, socket))
        {
            throw new InvalidOperationException("Failed to register WebSocket connection");
        }

        return id;
    }

    public void Unregister(Guid connectionId) => _connections.TryRemove(connectionId, out _);

    public long IncrementGeneratedMessages() => Interlocked.Increment(ref _generatedMessages);

    public bool ShouldDuplicate(long messageNumber) =>
        DuplicatesEnabled && messageNumber % _options.DuplicateEvery == 0;

    public void SetDuplicates(bool enabled) => Volatile.Write(ref _duplicatesEnabled, enabled ? 1 : 0);

    public void SetPaused(bool paused) => Volatile.Write(ref _paused, paused ? 1 : 0);

    public void Remember(byte[] payload)
    {
        lock (_recentGate)
        {
            _recentPayloads.Enqueue(payload);

            while (_recentPayloads.Count > _options.RecentBufferSize)
            {
                _recentPayloads.Dequeue();
            }
        }
    }

    public IReadOnlyList<byte[]> GetReplayPayloads()
    {
        if (_options.ReplayCountOnConnect == 0)
        {
            return [];
        }

        lock (_recentGate)
        {
            return _recentPayloads
                .Skip(Math.Max(0, _recentPayloads.Count - _options.ReplayCountOnConnect))
                .ToArray();
        }
    }

    public int DisconnectAll()
    {
        var count = 0;

        foreach (var socket in _connections.Values)
        {
            try
            {
                socket.Abort();
                count++;
            }
            catch (ObjectDisposedException)
            {
                // Обработчик запроса уже занимается очисткой
            }
        }

        return count;
    }
}
