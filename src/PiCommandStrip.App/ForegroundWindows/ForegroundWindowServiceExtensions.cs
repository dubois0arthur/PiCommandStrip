using PiCommandStrip.App.WebSockets;

namespace PiCommandStrip.App.ForegroundWindows;

public static class ForegroundWindowServiceExtensions
{
    public static IServiceCollection AddForegroundWindowMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<IForegroundWindowProvider, WindowsForegroundWindowProvider>();
        services.AddSingleton<ForegroundStateStore>();
        services.AddSingleton<IPcStateBroadcaster, WebSocketPcStateBroadcaster>();
        services.AddSingleton<ForegroundStateMonitor>();
        services.AddHostedService<ForegroundWindowPollingService>();

        return services;
    }
}
