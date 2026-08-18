using System.Net;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.Spotify;

public sealed record SpotifyConfiguration(
    bool Enabled,
    bool IsConfigured,
    string ClientId,
    string ClientSecret,
    Uri RedirectUri,
    string? ConfigurationIssue)
{
    public static readonly IReadOnlyList<string> RequiredScopes =
    [
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-library-read",
        "user-library-modify"
    ];

    public string ScopeParameter => string.Join(' ', RequiredScopes);

    public static SpotifyConfiguration Create(SpotifyOptions options, int serverPort)
    {
        var defaultRedirect = new Uri(
            $"http://127.0.0.1:{serverPort}/spotify/auth/callback",
            UriKind.Absolute);
        var hasConfiguredRedirect = !string.IsNullOrWhiteSpace(options.RedirectUri);
        var validConfiguredRedirect = Uri.TryCreate(
            options.RedirectUri,
            UriKind.Absolute,
            out var configuredRedirect);
        var redirectUri = validConfiguredRedirect ? configuredRedirect! : defaultRedirect;

        if (!options.Enabled)
        {
            return new(false, false, string.Empty, string.Empty, redirectUri, null);
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return new(
                true,
                false,
                string.Empty,
                string.Empty,
                redirectUri,
                "ClientId and ClientSecret are required when Spotify is enabled.");
        }

        if (hasConfiguredRedirect && !validConfiguredRedirect)
        {
            return new(
                true,
                false,
                options.ClientId,
                options.ClientSecret,
                defaultRedirect,
                "RedirectUri must be an absolute URI.");
        }

        if (!IsPermittedRedirectUri(redirectUri))
        {
            return new(
                true,
                false,
                options.ClientId,
                options.ClientSecret,
                redirectUri,
                "RedirectUri must use HTTPS or an explicit loopback IP address.");
        }

        return new(
            true,
            true,
            options.ClientId.Trim(),
            options.ClientSecret,
            redirectUri,
            null);
    }

    private static bool IsPermittedRedirectUri(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp &&
            IPAddress.TryParse(uri.Host, out var address) &&
            IPAddress.IsLoopback(address);
    }
}
