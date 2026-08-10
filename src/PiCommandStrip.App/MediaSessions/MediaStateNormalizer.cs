namespace PiCommandStrip.App.MediaSessions;

public sealed class MediaStateNormalizer
{
    public MediaState Normalize(
        MediaSessionSnapshot? snapshot,
        DateTimeOffset lastUpdatedUtc)
    {
        if (snapshot is null)
        {
            return MediaState.Inactive(lastUpdatedUtc);
        }

        var totalDuration = NormalizeDuration(snapshot.TotalDuration);
        var position = NormalizePosition(snapshot.Position, totalDuration);

        return new MediaState(
            true,
            NormalizeText(snapshot.SessionSourceIdentifier),
            NormalizeText(snapshot.SourceName),
            NormalizeText(snapshot.Title),
            NormalizeText(snapshot.Artist),
            NormalizeText(snapshot.AlbumTitle),
            NormalizePlaybackState(snapshot.PlaybackStatus),
            position,
            totalDuration,
            snapshot.SupportsPrevious,
            snapshot.SupportsNext,
            snapshot.SupportsPlay,
            snapshot.SupportsPause,
            snapshot.SupportsPlayPause,
            snapshot.SupportsSeeking,
            lastUpdatedUtc,
            NormalizeArtworkId(snapshot.ArtworkId));
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeArtworkId(string? value) =>
        MediaArtworkCache.IsValidArtworkId(value) ? value : null;

    private static TimeSpan? NormalizeDuration(TimeSpan? duration) =>
        duration is { } value && value > TimeSpan.Zero ? value : null;

    private static TimeSpan? NormalizePosition(
        TimeSpan? position,
        TimeSpan? totalDuration)
    {
        if (position is null)
        {
            return null;
        }

        var normalized = position.Value < TimeSpan.Zero
            ? TimeSpan.Zero
            : position.Value;
        return totalDuration is { } duration && normalized > duration
            ? duration
            : normalized;
    }

    private static string NormalizePlaybackState(MediaPlaybackStatus status) => status switch
    {
        MediaPlaybackStatus.Closed => MediaPlaybackStates.Closed,
        MediaPlaybackStatus.Opened => MediaPlaybackStates.Opened,
        MediaPlaybackStatus.Changing => MediaPlaybackStates.Changing,
        MediaPlaybackStatus.Stopped => MediaPlaybackStates.Stopped,
        MediaPlaybackStatus.Playing => MediaPlaybackStates.Playing,
        MediaPlaybackStatus.Paused => MediaPlaybackStates.Paused,
        _ => MediaPlaybackStates.Unknown
    };
}
