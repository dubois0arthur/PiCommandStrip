using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.Tests.Spotify;

public sealed class SpotifyConfigurationTests
{
    [Fact]
    public void Create_Disabled_RemainsOptionalWithoutCredentials()
    {
        var configuration = SpotifyConfiguration.Create(new SpotifyOptions(), 5077);

        Assert.False(configuration.Enabled);
        Assert.False(configuration.IsConfigured);
        Assert.Equal("http://127.0.0.1:5077/spotify/auth/callback", configuration.RedirectUri.AbsoluteUri);
    }

    [Fact]
    public void Create_EnabledWithInvalidExplicitRedirect_DoesNotSilentlyUseDefault()
    {
        var configuration = SpotifyConfiguration.Create(
            new SpotifyOptions
            {
                Enabled = true,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "not a URI"
            },
            5077);

        Assert.False(configuration.IsConfigured);
        Assert.Contains("absolute URI", configuration.ConfigurationIssue);
    }

    [Fact]
    public void RequiredScopes_AreTheMinimumFeatureScopes()
    {
        Assert.Equal(
            [
                "user-read-playback-state",
                "user-modify-playback-state",
                "user-library-read",
                "user-library-modify"
            ],
            SpotifyConfiguration.RequiredScopes);
    }
}
