namespace PiCommandStrip.App.Spotify;

public sealed record SpotifyCommandResult(bool Succeeded, string Message)
{
    public static SpotifyCommandResult Success(string message) => new(true, message);

    public static SpotifyCommandResult Failure(string message) => new(false, message);
}

public interface ISpotifyService
{
    SpotifyState Current { get; }

    Task<SpotifyCommandResult> SetSavedAsync(
        bool isSaved,
        CancellationToken cancellationToken);

    Task<SpotifyCommandResult> SetShuffleAsync(
        bool enabled,
        CancellationToken cancellationToken);

    Task<SpotifyCommandResult> SetRepeatAsync(
        string repeatState,
        CancellationToken cancellationToken);
}

public interface ISpotifyStateBroadcaster
{
    Task BroadcastAsync(SpotifyState state, CancellationToken cancellationToken);
}
