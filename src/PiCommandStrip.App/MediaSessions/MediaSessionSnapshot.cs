namespace PiCommandStrip.App.MediaSessions;

public sealed record MediaSessionSnapshot(
    string? SessionSourceIdentifier,
    string? SourceName,
    string? Title,
    string? Artist,
    string? AlbumTitle,
    MediaPlaybackStatus PlaybackStatus,
    TimeSpan? Position,
    TimeSpan? TotalDuration,
    bool SupportsPrevious,
    bool SupportsNext,
    bool SupportsPlay,
    bool SupportsPause,
    bool SupportsPlayPause,
    bool SupportsSeeking,
    string? ArtworkId = null);
