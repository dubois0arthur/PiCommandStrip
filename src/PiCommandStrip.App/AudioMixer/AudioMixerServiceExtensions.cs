namespace PiCommandStrip.App.AudioMixer;

public static class AudioMixerServiceExtensions
{
    public static IServiceCollection AddWindowsAudioMixerMonitoring(
        this IServiceCollection services)
    {
        services.AddSingleton<AudioStateNormalizer>();
        services.AddSingleton<AudioStateStore>();
        services.AddSingleton<WindowsAudioMixerService>();
        services.AddSingleton<IAudioMixerService>(services =>
            services.GetRequiredService<WindowsAudioMixerService>());
        services.AddHostedService(services =>
            services.GetRequiredService<WindowsAudioMixerService>());

        return services;
    }
}
