using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.Protocol;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.App.WebSockets;

public static class WebSocketEndpointExtensions
{
    public static IServiceCollection AddPiCommandStripWebSockets(
        this IServiceCollection services,
        TimeSpan commandCooldown)
    {
        services.AddSingleton<ClientMessageParser>();
        services.AddSingleton<ServerMessageFactory>();
        services.AddSingleton<WebSocketMessageReader>();
        services.AddSingleton(services => new WebSocketConnectionManager(
            services.GetRequiredService<ServerMessageFactory>(),
            services.GetRequiredService<TimeProvider>(),
            commandCooldown,
            services.GetRequiredService<ILogger<WebSocketConnectionManager>>()));
        services.AddSingleton<IContextStateBroadcaster, WebSocketContextStateBroadcaster>();
        services.AddSingleton<IMediaStateBroadcaster, WebSocketMediaStateBroadcaster>();
        services.AddSingleton<IAudioStateBroadcaster, WebSocketAudioStateBroadcaster>();
        services.AddSingleton<ISpotifyStateBroadcaster, WebSocketSpotifyStateBroadcaster>();
        services.AddSingleton<IBrowserStateBroadcaster, WebSocketBrowserStateBroadcaster>();
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
