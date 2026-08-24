using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Application.Hosting;
using BrokerPilot.MarketData.Application.Services;
using BrokerPilot.MarketData.Infrastructure.Exchange;
using BrokerPilot.MarketData.Infrastructure.Exchange.Parsers;
using BrokerPilot.MarketData.Infrastructure.Exchange.Transport;
using BrokerPilot.MarketData.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});

var marketDataOptions = builder.Configuration
    .GetSection(MarketDataOptions.SectionName)
    .Get<MarketDataOptions>()
    ?? throw new InvalidOperationException("MarketData configuration section is missing.");
marketDataOptions.Validate();

var connectionString = builder.Configuration.GetConnectionString("MarketData")
    ?? throw new InvalidOperationException("ConnectionStrings:MarketData is missing.");

// Таймаут остановки самого хоста обязан быть больше времени drain'а. Иначе реальным окном
// drain'а окажется дефолт хоста, а не значение из конфигурации, и связь между этими двумя
// числами будет держаться на совпадении
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = marketDataOptions.DrainTimeout + marketDataOptions.ShutdownHeadroom);

builder.Services.AddSingleton(marketDataOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAggregatorMetrics, AggregatorMetrics>();
builder.Services.AddSingleton<ITickDeduplicator, ConcurrentTickDeduplicator>();
builder.Services.AddSingleton<ITickQueue, BoundedTickQueue>();
builder.Services.AddSingleton<IExchangeMessageParser, AlphaExchangeMessageParser>();
builder.Services.AddSingleton<IExchangeMessageParser, BetaExchangeMessageParser>();
builder.Services.AddSingleton<IExchangeMessageParser, GammaExchangeMessageParser>();
builder.Services.AddSingleton<ExchangeMessageParserResolver>();
builder.Services.AddSingleton<IWebSocketClientFactory, ClientWebSocketClientFactory>();
builder.Services.AddSingleton<IReconnectDelayProvider, ExponentialReconnectDelayProvider>();
builder.Services.AddSingleton<IExchangeFeedFactory, ExchangeFeedFactory>();
builder.Services.AddSingleton<IDatabaseRetryDelayProvider, ExponentialDatabaseRetryDelayProvider>();
builder.Services.AddSingleton<ITransientDatabaseErrorDetector, NpgsqlTransientErrorDetector>();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ITickRepository, PostgresTickRepository>();
builder.Services.AddSingleton<IDeadLetterStore, FileDeadLetterStore>();
builder.Services.AddSingleton<ITickBatchWriter, ResilientTickBatchWriter>();
builder.Services.AddSingleton<ITickBatchConsumer, TickBatchConsumer>();

// Hosted-сервисы останавливаются в порядке, обратном регистрации. Heartbeat зарегистрирован
// первым, значит останавливается последним и продолжает писать метрики всё время, пока идёт
// drain пайплайна - иначе самый интересный отрезок логов отсутствовал бы
builder.Services.AddHostedService<MetricsHeartbeatHostedService>();
builder.Services.AddHostedService<MarketDataPipelineHostedService>();

var app = builder.Build();

app.MapGet("/health", (IAggregatorMetrics metrics, ITickQueue queue, ITickDeduplicator deduplicator) =>
{
    var snapshot = metrics.Snapshot();
    var allSourcesConnected = marketDataOptions.Sources.All(source =>
        snapshot.SourceConnections.TryGetValue(source.Name, out var connected) && connected);

    var payload = new
    {
        status = allSourcesConnected ? "healthy" : "degraded",
        allSourcesConnected,
        queueDepth = queue.Count,
        queueCapacity = queue.Capacity,
        dedupEntries = deduplicator.ApproximateEntryCount
    };

    // Код ответа, а не только поле в теле: probe (Docker, Kubernetes, балансировщик) читает
    // статус, а не JSON. Health-эндпоинт, всегда отвечающий 200, для probe бесполезен
    return allSourcesConnected
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/metrics", (IAggregatorMetrics metrics, ITickQueue queue, ITickDeduplicator deduplicator) =>
{
    var snapshot = metrics.Snapshot();

    return Results.Ok(new
    {
        snapshot.RawTicksReceived,
        snapshot.TicksAccepted,
        snapshot.DuplicateTicks,
        snapshot.InvalidTicks,
        snapshot.TicksWritten,
        snapshot.DatabaseConflicts,
        snapshot.DatabaseWriteAttemptFailures,
        snapshot.TicksDeadLettered,
        snapshot.TicksDropped,
        snapshot.ReconnectsScheduled,
        snapshot.SourceConnections,
        queueDepth = queue.Count,
        queueCapacity = queue.Capacity,
        queueFillRatio = queue.Capacity == 0 ? 0 : (double)queue.Count / queue.Capacity,
        dedupEntries = deduplicator.ApproximateEntryCount
    });
});

app.Run();
