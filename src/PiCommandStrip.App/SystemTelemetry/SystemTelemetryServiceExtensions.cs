using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.SystemTelemetry;

public static class SystemTelemetryServiceExtensions
{
    public static IServiceCollection AddSystemTelemetryMonitoring(
        this IServiceCollection services,
        SystemTelemetryConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddSingleton<IHardwareTelemetryProvider, WindowsHardwareTelemetryProvider>();
        services.AddSingleton<SystemTelemetryNormalizer>();
        services.AddSingleton<SystemTelemetryStateStore>();
        services.AddSingleton<SystemTelemetryService>();
        services.AddSingleton<ISystemTelemetryService>(services =>
            services.GetRequiredService<SystemTelemetryService>());
        services.AddHostedService(services =>
            services.GetRequiredService<SystemTelemetryService>());
        return services;
    }
}
