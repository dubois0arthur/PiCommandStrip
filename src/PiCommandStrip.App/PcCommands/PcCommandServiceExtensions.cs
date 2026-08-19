using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.Spotify;
using PiCommandStrip.App.ResearchInbox;

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
        services.AddSingleton<IPcCommandHandler>(services =>
            new AudioCommandHandler(
                PcCommandIds.AudioSetMasterVolume,
                services.GetRequiredService<IAudioMixerService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new AudioCommandHandler(
                PcCommandIds.AudioSetMasterMute,
                services.GetRequiredService<IAudioMixerService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new AudioCommandHandler(
                PcCommandIds.AudioSetApplicationVolume,
                services.GetRequiredService<IAudioMixerService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new AudioCommandHandler(
                PcCommandIds.AudioSetApplicationMute,
                services.GetRequiredService<IAudioMixerService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new AudioCommandHandler(
                PcCommandIds.AudioSetOutputDevice,
                services.GetRequiredService<IAudioMixerService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new SpotifyCommandHandler(
                PcCommandIds.SpotifySetSaved,
                services.GetRequiredService<ISpotifyService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new SpotifyCommandHandler(
                PcCommandIds.SpotifySetShuffle,
                services.GetRequiredService<ISpotifyService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new SpotifyCommandHandler(
                PcCommandIds.SpotifySetRepeat,
                services.GetRequiredService<ISpotifyService>()));
        foreach (var commandId in PcCommandIds.BrowserCommands)
        {
            services.AddSingleton<IPcCommandHandler>(services =>
                new BrowserCommandHandler(
                    commandId,
                    services.GetRequiredService<IBrowserCommandService>()));
        }
        services.AddSingleton<IPcCommandHandler>(services =>
            new ResearchInboxCommandHandler(
                PcCommandIds.ResearchSaveCurrent,
                services.GetRequiredService<IResearchInboxService>(),
                services.GetRequiredService<IBrowserIntegrationService>(),
                services.GetRequiredService<IBrowserCommandService>()));
        services.AddSingleton<IPcCommandHandler>(services =>
            new ResearchInboxCommandHandler(
                PcCommandIds.ResearchOpenItem,
                services.GetRequiredService<IResearchInboxService>(),
                services.GetRequiredService<IBrowserIntegrationService>(),
                services.GetRequiredService<IBrowserCommandService>()));
        services.AddSingleton<IPcCommandDispatcher, PcCommandDispatcher>();

        return services;
    }
}
