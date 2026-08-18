using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.Spotify;

public static class SpotifyStateFactory
{
    private const int MaximumQueueItems = 5;

    public static SpotifyState Available(
        MediaState mediaState,
        SpotifyPlaybackSnapshot playback,
        bool? isSaved,
        IReadOnlyList<SpotifyQueueItemState>? queue,
        DateTimeOffset timestamp)
    {
        var applies = SpotifyMediaMatcher.MatchesCurrentItem(mediaState, playback);
        return new SpotifyState(
            SpotifyStatuses.Available,
            true,
            true,
            applies,
            applies ? playback.ItemUri : null,
            applies ? playback.ItemType : null,
            applies ? mediaState.Title : null,
            applies ? isSaved : null,
            applies ? playback.ShuffleEnabled : null,
            applies ? playback.RepeatState : null,
            applies ? playback.Device : null,
            applies
                ? (queue ?? []).Take(MaximumQueueItems).ToArray()
                : [],
            timestamp);
    }

    public static SpotifyState Failure(
        MediaState mediaState,
        SpotifyState previous,
        string status,
        DateTimeOffset timestamp,
        DateTimeOffset? retryAfterUtc = null)
    {
        var preserve = SpotifyMediaMatcher.IsSpotifySource(mediaState) &&
            previous.AppliesToCurrentMedia;
        return previous with
        {
            Status = status,
            IsConfigured = true,
            IsAuthenticated = true,
            AppliesToCurrentMedia = preserve,
            LastUpdatedUtc = timestamp,
            RetryAfterUtc = retryAfterUtc
        };
    }
}
