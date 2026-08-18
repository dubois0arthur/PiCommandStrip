namespace PiCommandStrip.App.Spotify;

public interface ISpotifyApiClient
{
    Task<SpotifyTokenResponse> ExchangeAuthorizationCodeAsync(
        string code,
        CancellationToken cancellationToken);

    Task<SpotifyTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<SpotifyPlaybackSnapshot?> GetPlaybackAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<bool> IsSavedAsync(
        string accessToken,
        string itemUri,
        CancellationToken cancellationToken);

    Task SetSavedAsync(
        string accessToken,
        string itemUri,
        bool isSaved,
        CancellationToken cancellationToken);

    Task<SpotifyQueueSnapshot> GetQueueAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task SetShuffleAsync(
        string accessToken,
        bool enabled,
        CancellationToken cancellationToken);

    Task SetRepeatAsync(
        string accessToken,
        string repeatState,
        CancellationToken cancellationToken);
}
