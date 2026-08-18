namespace PiCommandStrip.App.Spotify;

public sealed record StoredSpotifyAuthorization(
    string RefreshToken,
    DateTimeOffset AuthorizedAtUtc);

public interface ISpotifyTokenStore
{
    Task<StoredSpotifyAuthorization?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        StoredSpotifyAuthorization authorization,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
