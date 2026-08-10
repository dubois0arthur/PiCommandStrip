namespace PiCommandStrip.App.MediaSessions;

public static class MediaSessionServiceExtensions
{
    public static IServiceCollection AddWindowsMediaSessionMonitoring(
        this IServiceCollection services)
    {
        services.AddSingleton<MediaStateNormalizer>();
        services.AddSingleton<MediaStateStore>();
        services.AddSingleton<IMediaArtworkCache, MediaArtworkCache>();
        services.AddSingleton<WindowsMediaSessionService>();
        services.AddSingleton<IMediaSessionService>(services =>
            services.GetRequiredService<WindowsMediaSessionService>());
        services.AddHostedService(services =>
            services.GetRequiredService<WindowsMediaSessionService>());

        return services;
    }
}
