using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public static class WebSocketEndpointExtensions
{
    public static IServiceCollection AddPiCommandStripWebSockets(this IServiceCollection services)
    {
        services.AddSingleton<ClientMessageParser>();
        services.AddSingleton<ServerMessageFactory>();
        services.AddSingleton<WebSocketMessageReader>();
        services.AddSingleton<WebSocketConnectionManager>();
        services.AddTransient<WebSocketConnectionHandler>();

        return services;
    }

    public static IEndpointConventionBuilder MapPiCommandStripWebSocket(this IEndpointRouteBuilder endpoints) =>
        endpoints.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "A WebSocket upgrade request is required."
                });
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionManager = context.RequestServices.GetRequiredService<WebSocketConnectionManager>();
            var handler = context.RequestServices.GetRequiredService<WebSocketConnectionHandler>();
            var connection = connectionManager.Add(socket);

            try
            {
                await handler.HandleAsync(connection, context.RequestAborted);
            }
            finally
            {
                connectionManager.Remove(connection.ConnectionId);
            }
        });
}
