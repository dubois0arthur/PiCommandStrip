namespace PiCommandStrip.App.MediaSessions;

public sealed record MediaState(
    bool HasActiveSession,
    string? SessionSourceIdentifier,
    string? SourceName,
    string? Title,
    string? Artist,
    string? AlbumTitle,
    string PlaybackState,
    TimeSpan? Position,
    TimeSpan? TotalDuration,
    bool SupportsPrevious,
    bool SupportsNext,
    bool SupportsPlay,
    bool SupportsPause,
    bool SupportsPlayPause,
    bool SupportsSeeking,
    DateTimeOffset LastUpdatedUtc,
    string? ArtworkId = null)
{
    public static MediaState Inactive(DateTimeOffset lastUpdatedUtc) =>
        new(
            false,
            null,
            null,
            null,
            null,
            null,
            MediaPlaybackStates.None,
            null,
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            lastUpdatedUtc);

    public bool HasSameMeaningAs(MediaState other) =>
        HasActiveSession == other.HasActiveSession &&
        string.Equals(
            SessionSourceIdentifier,
            other.SessionSourceIdentifier,
            StringComparison.Ordinal) &&
        string.Equals(SourceName, other.SourceName, StringComparison.Ordinal) &&
        string.Equals(Title, other.Title, StringComparison.Ordinal) &&
        string.Equals(Artist, other.Artist, StringComparison.Ordinal) &&
        string.Equals(AlbumTitle, other.AlbumTitle, StringComparison.Ordinal) &&
        string.Equals(PlaybackState, other.PlaybackState, StringComparison.Ordinal) &&
        Position == other.Position &&
        TotalDuration == other.TotalDuration &&
        SupportsPrevious == other.SupportsPrevious &&
        SupportsNext == other.SupportsNext &&
        SupportsPlay == other.SupportsPlay &&
        SupportsPause == other.SupportsPause &&
        SupportsPlayPause == other.SupportsPlayPause &&
        SupportsSeeking == other.SupportsSeeking &&
        string.Equals(ArtworkId, other.ArtworkId, StringComparison.Ordinal);
}
