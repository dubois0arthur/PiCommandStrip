using System.Net;

namespace PiCommandStrip.App.Spotify;

public sealed class SpotifyTokenManager(
    SpotifyConfiguration configuration,
    ISpotifyTokenStore tokenStore,
    ISpotifyApiClient apiClient,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;
    private StoredSpotifyAuthorization? _authorization;
    private bool _loaded;

    public async Task<bool> HasAuthorizationAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _authorization is not null;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!configuration.IsConfigured)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
            if (_authorization is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessTokenExpiresAtUtc - RefreshSkew > timeProvider.GetUtcNow())
            {
                return _accessToken;
            }

            try
            {
                var token = await apiClient.RefreshAccessTokenAsync(
                    _authorization.RefreshToken,
                    cancellationToken);
                await ApplyTokenAsync(token, _authorization.AuthorizedAtUtc, cancellationToken);
                return _accessToken;
            }
            catch (SpotifyApiException exception)
                when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                await ClearCoreAsync(cancellationToken);
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CompleteAuthorizationAsync(
        string code,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var token = await apiClient.ExchangeAuthorizationCodeAsync(code, cancellationToken);
            ValidateGrantedScopes(token.Scope);
            await ApplyTokenAsync(token, timeProvider.GetUtcNow(), cancellationToken);
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidateAccessToken()
    {
        _accessToken = null;
        _accessTokenExpiresAtUtc = default;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _authorization = await tokenStore.LoadAsync(cancellationToken);
        _loaded = true;
    }

    private async Task ApplyTokenAsync(
        SpotifyTokenResponse token,
        DateTimeOffset authorizedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new SpotifyApiException("store authorization", HttpStatusCode.BadGateway);
        }

        _accessToken = token.AccessToken;
        _accessTokenExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(
            Math.Max(60, token.ExpiresInSeconds));
        var nextAuthorization = new StoredSpotifyAuthorization(
            token.RefreshToken,
            authorizedAtUtc);
        if (_authorization != nextAuthorization)
        {
            await tokenStore.SaveAsync(nextAuthorization, cancellationToken);
        }

        _authorization = nextAuthorization;
    }

    private async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        _accessToken = null;
        _accessTokenExpiresAtUtc = default;
        _authorization = null;
        _loaded = true;
        await tokenStore.ClearAsync(cancellationToken);
    }

    private static void ValidateGrantedScopes(string scope)
    {
        var granted = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var missing = SpotifyConfiguration.RequiredScopes
            .Where(required => !granted.Contains(required))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Spotify did not grant the required scopes: {string.Join(", ", missing)}.");
        }
    }
}
