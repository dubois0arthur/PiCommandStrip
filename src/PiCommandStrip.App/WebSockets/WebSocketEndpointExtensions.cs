using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public static class WebSocketEndpointExtensions
{
    public static IServiceCollection AddPiCommandStripWebSockets(this IServiceCollection services)
    {
        services.AddSingleton<ClientMessageParser>();
        services.AddSingleton<ServerMessageFactory>();
        services.AddSingleton<WebSocketMessageReader>();
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
            var handler = context.RequestServices.GetRequiredService<WebSocketConnectionHandler>();
            await handler.HandleAsync(socket, context.RequestAborted);
        });
}
