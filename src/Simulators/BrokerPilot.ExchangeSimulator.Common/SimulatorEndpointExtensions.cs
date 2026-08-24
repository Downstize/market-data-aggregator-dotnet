using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BrokerPilot.ExchangeSimulator.Common;

public static class SimulatorEndpointExtensions
{
    public static WebApplication MapExchangeSimulator(this WebApplication app)
    {
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(10)
        });

        app.MapGet("/health", (IQuoteFormatter formatter, ExchangeSimulationState state) => Results.Ok(new
        {
            status = "ok",
            exchange = formatter.ExchangeName,
            activeConnections = state.ActiveConnections
        }));

        app.MapGet("/admin/status", (IQuoteFormatter formatter, ExchangeSimulationState state) => Results.Ok(new
        {
            exchange = formatter.ExchangeName,
            state.ActiveConnections,
            state.DuplicatesEnabled,
            state.Paused,
            state.GeneratedMessages
        }));

        app.MapPost("/admin/duplicates/{enabled:bool}",
            (bool enabled, IQuoteFormatter formatter, ExchangeSimulationState state) =>
            {
                state.SetDuplicates(enabled);
                return Results.Ok(new
                {
                    exchange = formatter.ExchangeName,
                    duplicatesEnabled = enabled
                });
            });

        app.MapPost("/admin/pause/{enabled:bool}",
            (bool enabled, IQuoteFormatter formatter, ExchangeSimulationState state) =>
            {
                state.SetPaused(enabled);
                return Results.Ok(new
                {
                    exchange = formatter.ExchangeName,
                    paused = enabled
                });
            });

        app.MapPost("/admin/disconnect", (IQuoteFormatter formatter, ExchangeSimulationState state) =>
        {
            var disconnected = state.DisconnectAll();
            return Results.Ok(new
            {
                exchange = formatter.ExchangeName,
                disconnected
            });
        });

        app.MapGet("/ws", async context =>
        {
            var handler = context.RequestServices.GetRequiredService<ExchangeWebSocketHandler>();
            await handler.HandleAsync(context).ConfigureAwait(false);
        });

        return app;
    }
}
