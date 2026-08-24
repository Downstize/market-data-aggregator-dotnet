using BrokerPilot.ExchangeSimulator.Beta;
using BrokerPilot.ExchangeSimulator.Common;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Simulation").Get<SimulationOptions>() ?? new SimulationOptions();
options.Validate();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IQuoteFormatter, BetaQuoteFormatter>();
builder.Services.AddSingleton<MarketQuoteGenerator>();
builder.Services.AddSingleton<ExchangeSimulationState>();
builder.Services.AddSingleton<ExchangeWebSocketHandler>();

var app = builder.Build();
app.MapExchangeSimulator();
app.Run();
