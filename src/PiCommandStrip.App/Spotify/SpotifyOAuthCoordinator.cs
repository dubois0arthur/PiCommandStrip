using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace PiCommandStrip.App.Spotify;

public sealed record SpotifyAuthorizationResult(bool Succeeded, string Message);

public sealed class SpotifyOAuthCoordinator(
    SpotifyConfiguration configuration,
    SpotifyTokenManager tokenManager,
    TimeProvider timeProvider,
    ILogger<SpotifyOAuthCoordinator> logger)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly Lock _lock = new();
    private PendingAuthorization? _pending;

    public Uri BeginAuthorization()
    {
        if (!configuration.IsConfigured)
        {
            throw new InvalidOperationException("Spotify is not configured.");
        }

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _pending = new PendingAuthorization(state, timeProvider.GetUtcNow());
        }

        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = configuration.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri,
            ["state"] = state,
            ["scope"] = configuration.ScopeParameter,
            ["show_dialog"] = "true"
        };
        return new Uri(QueryHelpers.AddQueryString(
            "https://accounts.spotify.com/authorize",
            parameters));
    }

    public async Task<SpotifyAuthorizationResult> CompleteAuthorizationAsync(
        string? code,
        string? state,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return new(false, "Spotify authorization was declined or canceled.");
        }

        PendingAuthorization? pending;
        lock (_lock)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is null ||
            string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(state) ||
            timeProvider.GetUtcNow() - pending.CreatedAtUtc > StateLifetime ||
            !FixedTimeEquals(pending.State, state))
        {
            return new(false, "Spotify authorization state was missing, expired, or invalid.");
        }

        try
        {
            await tokenManager.CompleteAuthorizationAsync(code, cancellationToken);
            return new(true, "Spotify authorization completed. You can close this tab.");
        }
        catch (Exception exception) when (exception is SpotifyApiException or InvalidOperationException)
        {
            if (exception is SpotifyApiException apiException)
            {
                logger.LogWarning(
                    "Spotify authorization token exchange failed with HTTP status {SpotifyStatusCode}",
                    (int)apiException.StatusCode);
            }
            else
            {
                logger.LogWarning("Spotify authorization did not grant all required scopes");
            }
            return new(false, "Spotify authorization could not be completed. Check the host logs and configuration.");
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record PendingAuthorization(string State, DateTimeOffset CreatedAtUtc);
}
