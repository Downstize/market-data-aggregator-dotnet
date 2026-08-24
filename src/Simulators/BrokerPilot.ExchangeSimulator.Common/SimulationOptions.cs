namespace BrokerPilot.ExchangeSimulator.Common;

public sealed class SimulationOptions
{
    public int TicksPerSecond { get; init; } = 250;

    /// <summary>
    /// Как часто отправляется пачка котировок. Должно быть заметно выше гранулярности системного
    /// таймера: 20 мс дают 50 пачек в секунду - достаточно плавно, чтобы выглядеть потоком, и
    /// достаточно крупно, чтобы таймер реально успевал
    /// </summary>
    public int BurstIntervalMilliseconds { get; init; } = 20;

    public bool DuplicatesEnabledAtStartup { get; init; } = true;

    public int DuplicateEvery { get; init; } = 100;

    public int ReplayCountOnConnect { get; init; } = 5;

    public int RecentBufferSize { get; init; } = 200;

    public void Validate()
    {
        if (TicksPerSecond <= 0 || TicksPerSecond > 10_000)
        {
            throw new InvalidOperationException("Simulation:TicksPerSecond must be between 1 and 10000.");
        }

        if (BurstIntervalMilliseconds is < 5 or > 1_000)
        {
            throw new InvalidOperationException("Simulation:BurstIntervalMilliseconds must be between 5 and 1000.");
        }

        if (DuplicateEvery <= 0)
        {
            throw new InvalidOperationException("Simulation:DuplicateEvery must be positive.");
        }

        if (ReplayCountOnConnect < 0)
        {
            throw new InvalidOperationException("Simulation:ReplayCountOnConnect must not be negative.");
        }

        if (RecentBufferSize < ReplayCountOnConnect)
        {
            throw new InvalidOperationException("Simulation:RecentBufferSize must be >= ReplayCountOnConnect.");
        }
    }
}
