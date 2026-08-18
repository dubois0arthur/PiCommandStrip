namespace PiCommandStrip.App.Spotify;

public sealed record SpotifyPlaybackSnapshot(
    string? ItemUri,
    string? ItemType,
    string? Title,
    string? Subtitle,
    bool ShuffleEnabled,
    string RepeatState,
    SpotifyDeviceState? Device);

public sealed record SpotifyQueueSnapshot(
    IReadOnlyList<SpotifyQueueItemState> Items);

public sealed record SpotifyTokenResponse(
    string AccessToken,
    string RefreshToken,
    string Scope,
    int ExpiresInSeconds);

public sealed class SpotifyApiException(
    string operation,
    System.Net.HttpStatusCode statusCode,
    TimeSpan? retryAfter = null)
    : Exception($"Spotify operation '{operation}' failed with HTTP status {(int)statusCode}.")
{
    public string Operation { get; } = operation;

    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
