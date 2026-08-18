using System.Net;

namespace PiCommandStrip.App.BrowserIntegration;

public static class BrowserIntegrationServiceExtensions
{
    public static IServiceCollection AddBrowserIntegration(
        this IServiceCollection services,
        BrowserIntegrationConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddSingleton<BrowserStateNormalizer>();
        services.AddSingleton<BrowserStateStore>();
        services.AddSingleton<BrowserIntegrationAuthenticationService>();
        services.AddSingleton<BrowserAuthenticationAttemptLimiter>();
        services.AddSingleton<BrowserIntegrationMessageParser>();
        services.AddSingleton<BrowserIntegrationService>();
        services.AddSingleton<IBrowserIntegrationService>(services =>
            services.GetRequiredService<BrowserIntegrationService>());
        services.AddTransient<BrowserIntegrationConnectionHandler>();
        return services;
    }

    public static IEndpointConventionBuilder MapBrowserIntegration(this IEndpointRouteBuilder endpoints) =>
        endpoints.Map("/browser-integration/ws", async context =>
        {
            var configuration = context.RequestServices
                .GetRequiredService<BrowserIntegrationConfiguration>();
            var localAddress = context.Connection.LocalIpAddress;
            var remoteAddress = context.Connection.RemoteIpAddress;
            if (!configuration.Enabled ||
                context.Connection.LocalPort != configuration.Port ||
                localAddress is null || !IPAddress.IsLoopback(localAddress) ||
                remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (!IsExtensionOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var handler = context.RequestServices
                .GetRequiredService<BrowserIntegrationConnectionHandler>();
            await handler.HandleAsync(socket, context.RequestAborted);
        });

    private static bool IsExtensionOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        (uri.Scheme.Equals("moz-extension", StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase));
}
