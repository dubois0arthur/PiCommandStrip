using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.Tests.MediaSessions;

public sealed class MediaStateNormalizerTests
{
    private static readonly DateTimeOffset UpdatedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_WithoutCurrentSession_ReturnsInactiveState()
    {
        var state = new MediaStateNormalizer().Normalize(null, UpdatedAt);

        Assert.False(state.HasActiveSession);
        Assert.Equal(MediaPlaybackStates.None, state.PlaybackState);
        Assert.Null(state.SessionSourceIdentifier);
        Assert.Null(state.Position);
        Assert.Equal(UpdatedAt, state.LastUpdatedUtc);
    }

    [Fact]
    public void Normalize_WithIncompleteMetadata_PreservesAvailableSessionData()
    {
        var snapshot = CreateSnapshot() with
        {
            SessionSourceIdentifier = "  Spotify.exe  ",
            SourceName = " Spotify ",
            Title = "  Track title ",
            Artist = " ",
            AlbumTitle = null,
            PlaybackStatus = MediaPlaybackStatus.Paused
        };

        var state = new MediaStateNormalizer().Normalize(snapshot, UpdatedAt);

        Assert.True(state.HasActiveSession);
        Assert.Equal("Spotify.exe", state.SessionSourceIdentifier);
        Assert.Equal("Spotify", state.SourceName);
        Assert.Equal("Track title", state.Title);
        Assert.Null(state.Artist);
        Assert.Null(state.AlbumTitle);
        Assert.Equal(MediaPlaybackStates.Paused, state.PlaybackState);
    }

    [Theory]
    [InlineData(-5.0, 100.0, 0.0, 100.0)]
    [InlineData(125.0, 100.0, 100.0, 100.0)]
    [InlineData(25.0, 0.0, 25.0, null)]
    public void Normalize_ClampsPositionAndRejectsInvalidDuration(
        double positionSeconds,
        double durationSeconds,
        double expectedPositionSeconds,
        double? expectedDurationSeconds)
    {
        var snapshot = CreateSnapshot() with
        {
            Position = TimeSpan.FromSeconds(positionSeconds),
            TotalDuration = TimeSpan.FromSeconds(durationSeconds)
        };

        var state = new MediaStateNormalizer().Normalize(snapshot, UpdatedAt);

        Assert.Equal(TimeSpan.FromSeconds(expectedPositionSeconds), state.Position);
        Assert.Equal(
            expectedDurationSeconds is null
                ? null
                : TimeSpan.FromSeconds(expectedDurationSeconds.Value),
            state.TotalDuration);
    }

    [Fact]
    public void Normalize_PreservesReportedControlCapabilities()
    {
        var snapshot = CreateSnapshot() with
        {
            SupportsPrevious = true,
            SupportsNext = true,
            SupportsPlay = true,
            SupportsPause = false,
            SupportsPlayPause = true,
            SupportsSeeking = true
        };

        var state = new MediaStateNormalizer().Normalize(snapshot, UpdatedAt);

        Assert.True(state.SupportsPrevious);
        Assert.True(state.SupportsNext);
        Assert.True(state.SupportsPlay);
        Assert.False(state.SupportsPause);
        Assert.True(state.SupportsPlayPause);
        Assert.True(state.SupportsSeeking);
    }

    [Fact]
    public void Normalize_PreservesOnlyGeneratedArtworkIds()
    {
        var artworkId = new string('a', MediaArtworkCache.ArtworkIdLength);

        var valid = new MediaStateNormalizer().Normalize(
            CreateSnapshot() with { ArtworkId = artworkId },
            UpdatedAt);
        var invalid = new MediaStateNormalizer().Normalize(
            CreateSnapshot() with { ArtworkId = "../cover.jpg" },
            UpdatedAt);

        Assert.Equal(artworkId, valid.ArtworkId);
        Assert.Null(invalid.ArtworkId);
    }

    private static MediaSessionSnapshot CreateSnapshot() =>
        new(
            "source-id",
            "Source",
            "Title",
            "Artist",
            "Album",
            MediaPlaybackStatus.Playing,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            false,
            false,
            false,
            false,
            false,
            false);
}
