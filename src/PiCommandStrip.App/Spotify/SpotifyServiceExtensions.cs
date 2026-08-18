using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.Spotify;

public static class SpotifyServiceExtensions
{
    public static IServiceCollection AddSpotifyIntegration(
        this IServiceCollection services,
        SpotifyOptions options,
        int serverPort)
    {
        var configuration = SpotifyConfiguration.Create(options, serverPort);
        var dataProtectionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PiCommandStrip",
            "DataProtection-Keys");
        services.AddSingleton(configuration);
        services.AddDataProtection()
            .SetApplicationName("PiCommandStrip")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
            .ProtectKeysWithDpapi();
        services.AddSingleton<SpotifyStateStore>();
        services.AddSingleton<ISpotifyTokenStore, ProtectedSpotifyTokenStore>();
        services.AddHttpClient<SpotifyWebApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("PiCommandStrip", "1.0"));
        });
        services.AddSingleton<ISpotifyApiClient>(services =>
            services.GetRequiredService<SpotifyWebApiClient>());
        services.AddSingleton<SpotifyTokenManager>();
        services.AddSingleton<SpotifyOAuthCoordinator>();
        services.AddSingleton<SpotifyService>();
        services.AddSingleton<ISpotifyService>(services =>
            services.GetRequiredService<SpotifyService>());
        services.AddHostedService(services => services.GetRequiredService<SpotifyService>());
        return services;
    }
}
