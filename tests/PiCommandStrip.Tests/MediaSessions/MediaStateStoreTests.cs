using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.Tests.MediaSessions;

public sealed class MediaStateStoreTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryUpdate_WhenOnlyTimestampChanges_DoesNotPublishDuplicate()
    {
        var store = new MediaStateStore(new FixedTimeProvider(InitialTime));
        var first = CreateActiveState(InitialTime);
        var duplicate = first with { LastUpdatedUtc = InitialTime.AddSeconds(5) };

        Assert.True(store.TryUpdate(first, out var changed));
        Assert.False(store.TryUpdate(duplicate, out var retained));
        Assert.Same(first, changed);
        Assert.Same(first, retained);
        Assert.Equal(InitialTime, store.Current.LastUpdatedUtc);
    }

    [Theory]
    [InlineData("different title", "playing", 10)]
    [InlineData("Title", "paused", 10)]
    [InlineData("Title", "playing", 15)]
    public void TryUpdate_WhenMeaningfulMediaDataChanges_PublishesState(
        string title,
        string playbackState,
        int positionSeconds)
    {
        var store = new MediaStateStore(new FixedTimeProvider(InitialTime));
        var first = CreateActiveState(InitialTime);
        Assert.True(store.TryUpdate(first, out _));

        var next = first with
        {
            Title = title,
            PlaybackState = playbackState,
            Position = TimeSpan.FromSeconds(positionSeconds),
            LastUpdatedUtc = InitialTime.AddSeconds(5)
        };

        Assert.True(store.TryUpdate(next, out var changed));
        Assert.Same(next, changed);
        Assert.Same(next, store.Current);
    }

    [Fact]
    public void TryUpdate_WhenCurrentSessionDisappears_PublishesInactiveState()
    {
        var store = new MediaStateStore(new FixedTimeProvider(InitialTime));
        Assert.True(store.TryUpdate(CreateActiveState(InitialTime), out _));

        var inactive = MediaState.Inactive(InitialTime.AddSeconds(1));

        Assert.True(store.TryUpdate(inactive, out var changed));
        Assert.False(changed.HasActiveSession);
        Assert.Same(inactive, store.Current);
    }

    [Fact]
    public void TryUpdate_WhenArtworkChanges_PublishesState()
    {
        var store = new MediaStateStore(new FixedTimeProvider(InitialTime));
        var first = CreateActiveState(InitialTime) with { ArtworkId = new string('a', 64) };
        var next = first with
        {
            ArtworkId = new string('b', 64),
            LastUpdatedUtc = InitialTime.AddSeconds(1)
        };

        Assert.True(store.TryUpdate(first, out _));
        Assert.True(store.TryUpdate(next, out var changed));
        Assert.Equal(next.ArtworkId, changed.ArtworkId);
    }

    private static MediaState CreateActiveState(DateTimeOffset updatedAt) =>
        new(
            true,
            "Spotify.exe",
            "Spotify",
            "Title",
            "Artist",
            "Album",
            MediaPlaybackStates.Playing,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            true,
            true,
            false,
            true,
            true,
            true,
            updatedAt);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
