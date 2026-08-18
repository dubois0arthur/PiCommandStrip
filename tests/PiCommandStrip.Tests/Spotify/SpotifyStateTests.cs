using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.Tests.Spotify;

public sealed class SpotifyStateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Available_ConfidentSpotifyMatch_ExposesEnhancementAndCapsQueue()
    {
        var queue = Enumerable.Range(1, 8)
            .Select(index => new SpotifyQueueItemState($"Item {index}", null, "track"))
            .ToArray();

        var state = SpotifyStateFactory.Available(
            SpotifyMedia("Current item"),
            Playback("Current item"),
            true,
            queue,
            Now);

        Assert.True(state.AppliesToCurrentMedia);
        Assert.True(state.IsSaved);
        Assert.Equal(5, state.Queue.Count);
        Assert.Equal("spotify:track:current", state.ItemUri);
    }

    [Fact]
    public void Available_NonSpotifyMedia_DoesNotLeakSpotifyState()
    {
        var media = SpotifyMedia("Current item") with
        {
            SessionSourceIdentifier = "firefox.exe",
            SourceName = "Firefox"
        };

        var state = SpotifyStateFactory.Available(media, Playback("Current item"), true, [], Now);

        Assert.False(state.AppliesToCurrentMedia);
        Assert.Null(state.ItemUri);
        Assert.Null(state.IsSaved);
        Assert.Empty(state.Queue);
    }

    [Fact]
    public void Failure_ForMatchedSpotify_PreservesLastUsefulDataButMarksUnavailable()
    {
        var media = SpotifyMedia("Current item");
        var available = SpotifyStateFactory.Available(media, Playback("Current item"), true, [], Now);

        var failed = SpotifyStateFactory.Failure(
            media,
            available,
            SpotifyStatuses.Error,
            Now.AddSeconds(10));

        Assert.Equal(SpotifyStatuses.Error, failed.Status);
        Assert.True(failed.AppliesToCurrentMedia);
        Assert.True(failed.IsSaved);
    }

    [Fact]
    public void Store_TimestampOnlyChange_DoesNotBroadcastDuplicate()
    {
        var store = new SpotifyStateStore(new FixedTimeProvider(Now));
        var first = SpotifyState.Unauthenticated(Now);
        var duplicate = first with { LastUpdatedUtc = Now.AddSeconds(5) };

        Assert.True(store.TryUpdate(first, out _));
        Assert.False(store.TryUpdate(duplicate, out var retained));
        Assert.Equal(Now, retained.LastUpdatedUtc);
    }

    private static MediaState SpotifyMedia(string title) =>
        new(
            true,
            "Spotify.exe",
            "Spotify",
            title,
            "Artist",
            null,
            MediaPlaybackStates.Playing,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            true,
            true,
            true,
            true,
            true,
            true,
            Now);

    private static SpotifyPlaybackSnapshot Playback(string title) =>
        new(
            "spotify:track:current",
            "track",
            title,
            "Artist",
            true,
            SpotifyRepeatStates.Context,
            new SpotifyDeviceState("Office PC", "computer", false));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
