namespace BrokerPilot.MarketData.Application.Configuration;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>
    /// PostgreSQL принимает не более 65535 bind-параметров на один запрос, а multi-row INSERT
    /// связывает семь параметров на тик. Превышение не деградирует плавно - оно падает под
    /// нагрузкой в рантайме, поэтому лимит проверяется на старте
    /// </summary>
    private const int PostgresParameterLimit = 65535;
    private const int ParametersPerTick = 7;

    public static int MaxSupportedBatchSize => PostgresParameterLimit / ParametersPerTick;

    public int ChannelCapacity { get; init; } = 20_000;

    public int BatchSize { get; init; } = 500;

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan DeduplicationWindow { get; init; } = TimeSpan.FromMinutes(2);

    public int DeduplicationCleanupEvery { get; init; } = 10_000;

    public int DatabaseMaxRetries { get; init; } = 4;

    public TimeSpan DatabaseRetryInitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan DatabaseRetryMaxDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ReconnectInitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Сколько соединение должно прожить, чтобы счётчик backoff считался "восстановленным" и
    /// сбрасывался. Защита от флапающего источника, который иначе держал бы задержку на начальном значении вечно
    /// </summary>
    public TimeSpan ReconnectStabilityThreshold { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Запас поверх <see cref="DrainTimeout"/> при настройке HostOptions.ShutdownTimeout,
    /// чтобы хост никогда не убивал drain раньше, чем тот завершится по собственному таймауту
    /// </summary>
    public TimeSpan ShutdownHeadroom { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MetricsLogInterval { get; init; } = TimeSpan.FromSeconds(10);

    public int ReceiveBufferSize { get; init; } = 8 * 1024;

    public int MaxMessageBytes { get; init; } = 64 * 1024;

    public string DeadLetterDirectory { get; init; } = "data/dead-letter";

    public List<ExchangeSourceOptions> Sources { get; init; } = [];

    public void Validate()
    {
        if (ChannelCapacity <= 0)
        {
            throw new InvalidOperationException("MarketData:ChannelCapacity must be positive");
        }

        if (BatchSize <= 0 || BatchSize > ChannelCapacity)
        {
            throw new InvalidOperationException("MarketData:BatchSize must be positive and not exceed ChannelCapacity");
        }

        if (BatchSize > MaxSupportedBatchSize)
        {
            throw new InvalidOperationException(
                $"MarketData:BatchSize must not exceed {MaxSupportedBatchSize}: the multi-row INSERT binds " +
                $"{ParametersPerTick} parameters per tick and PostgreSQL allows at most {PostgresParameterLimit} per statement");
        }

        if (FlushInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:FlushInterval must be positive");
        }

        if (DeduplicationWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:DeduplicationWindow must be positive");
        }

        if (DeduplicationCleanupEvery <= 0)
        {
            throw new InvalidOperationException("MarketData:DeduplicationCleanupEvery must be positive");
        }

        if (DatabaseMaxRetries < 0)
        {
            throw new InvalidOperationException("MarketData:DatabaseMaxRetries must not be negative");
        }

        if (DatabaseRetryInitialDelay < TimeSpan.Zero || DatabaseRetryMaxDelay < DatabaseRetryInitialDelay)
        {
            throw new InvalidOperationException("Database retry delays are invalid");
        }

        if (ReconnectInitialDelay < TimeSpan.Zero || ReconnectMaxDelay < ReconnectInitialDelay)
        {
            throw new InvalidOperationException("Reconnect delays are invalid");
        }

        if (ReconnectStabilityThreshold <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:ReconnectStabilityThreshold must be positive");
        }

        if (DrainTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:DrainTimeout must be positive");
        }

        if (ShutdownHeadroom < TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:ShutdownHeadroom must not be negative");
        }

        if (MetricsLogInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("MarketData:MetricsLogInterval must be positive");
        }

        if (ReceiveBufferSize <= 0 || MaxMessageBytes < ReceiveBufferSize)
        {
            throw new InvalidOperationException("Receive buffer/message size settings are invalid");
        }

        if (string.IsNullOrWhiteSpace(DeadLetterDirectory))
        {
            throw new InvalidOperationException("MarketData:DeadLetterDirectory must be configured");
        }

        // Верхней границы намеренно нет. Задание описывает стенд из 2-3 бирж, но это свойство
        // конкретной конфигурации, а не системы: зашитый в код потолок противоречил бы
        // требованию "добавление новой биржи не должно требовать переписывания кода"
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("MarketData:Sources must contain at least one exchange source");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Sources)
        {
            source.Validate();

            if (!names.Add(source.Name))
            {
                throw new InvalidOperationException($"Duplicate exchange source name '{source.Name}'");
            }
        }
    }
}
