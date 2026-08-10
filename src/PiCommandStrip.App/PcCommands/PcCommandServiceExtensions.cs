using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.PcCommands;

public static class PcCommandServiceExtensions
{
    public static IServiceCollection AddPcCommands(this IServiceCollection services)
    {
        services.AddSingleton<INotepadLauncher, WindowsNotepadLauncher>();
        services.AddSingleton<IPcCommandHandler, OpenNotepadCommandHandler>();
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaPlay,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaPause,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaPlayPause,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaPrevious,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaNext,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new MediaCommandHandler(
                PcCommandIds.MediaSeek,
                services.GetRequiredService<IMediaSessionService>()));
        services.AddSingleton<IPcCommandDispatcher, PcCommandDispatcher>();

        return services;
    }
}
