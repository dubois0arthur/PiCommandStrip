using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.Tests.Spotify;

public sealed class SpotifyMediaMatcherTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    public void MatchesCurrentItem_SpotifySourceAndNormalizedExactTitle_ReturnsTrue(
        string sourceIdentifier,
        string sourceName)
    {
        var media = ActiveMedia(sourceIdentifier, sourceName, "Pretty (Ugly Before)");
        var playback = Playback("spotify:track:123", "Pretty — Ugly Before");

        Assert.True(SpotifyMediaMatcher.MatchesCurrentItem(media, playback));
    }

    [Fact]
    public void MatchesCurrentItem_BrowserMediaWithSameTitle_ReturnsFalse()
    {
        var media = ActiveMedia("firefox.exe", "Firefox", "Pretty (Ugly Before)");

        Assert.False(SpotifyMediaMatcher.MatchesCurrentItem(
            media,
            Playback("spotify:track:123", "Pretty (Ugly Before)")));
    }

    [Fact]
    public void MatchesCurrentItem_DifferentSpotifyTitle_ReturnsFalse()
    {
        var media = ActiveMedia("Spotify.exe", "Spotify", "First item");

        Assert.False(SpotifyMediaMatcher.MatchesCurrentItem(
            media,
            Playback("spotify:track:456", "Second item")));
    }

    private static MediaState ActiveMedia(string sourceId, string sourceName, string title) =>
        new(
            true,
            sourceId,
            sourceName,
            title,
            "Artist",
            null,
            MediaPlaybackStates.Playing,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            true,
            true,
            true,
            true,
            true,
            true,
            Now);

    private static SpotifyPlaybackSnapshot Playback(string uri, string title) =>
        new(
            uri,
            "track",
            title,
            "Artist",
            false,
            SpotifyRepeatStates.Off,
            new SpotifyDeviceState("Office PC", "computer", false));
}
