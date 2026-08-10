using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.Tests.MediaSessions;

public sealed class MediaArtworkCacheTests
{
    [Fact]
    public void Store_CreatesStableContentKeyAndSafeLocalUrl()
    {
        var cache = new MediaArtworkCache();
        byte[] source = [1, 2, 3, 4];

        var firstId = cache.Store(source, "image/png");
        var secondId = cache.Store([1, 2, 3, 4], "image/png; charset=binary");

        Assert.NotNull(firstId);
        Assert.Equal(firstId, secondId);
        Assert.True(MediaArtworkCache.IsValidArtworkId(firstId));
        Assert.Equal($"/media/artwork/{firstId}", MediaArtworkCache.CreateUrl(firstId));

        source[0] = 99;
        Assert.True(cache.TryGet(firstId, out var artwork));
        Assert.Equal([1, 2, 3, 4], artwork!.Bytes);
        Assert.Equal("image/png", artwork.ContentType);
    }

    [Fact]
    public void Store_ReplacesPreviousArtworkAndClearRemovesCurrentArtwork()
    {
        var cache = new MediaArtworkCache();
        var previousId = cache.Store([1], "image/jpeg");
        var currentId = cache.Store([2], "image/jpeg");

        Assert.NotEqual(previousId, currentId);
        Assert.False(cache.TryGet(previousId!, out _));
        Assert.True(cache.TryGet(currentId!, out _));

        cache.Clear();

        Assert.False(cache.TryGet(currentId!, out _));
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("ABCDEF")]
    [InlineData("")]
    public void TryGet_RejectsValuesThatAreNotGeneratedArtworkIds(string candidate)
    {
        var cache = new MediaArtworkCache();
        cache.Store([1], "image/png");

        Assert.False(cache.TryGet(candidate, out _));
    }

    [Fact]
    public void Store_RejectsUnsupportedOrOversizedContentAndEvictsStaleArtwork()
    {
        var cache = new MediaArtworkCache();
        var previousId = cache.Store([1], "image/png");

        Assert.Null(cache.Store([2], "image/svg+xml"));
        Assert.False(cache.TryGet(previousId!, out _));

        var oversized = new byte[MediaArtworkCache.MaximumArtworkBytes + 1];
        Assert.Null(cache.Store(oversized, "image/jpeg"));
    }

    [Fact]
    public void Store_InfersCommonBitmapTypeWhenWinRtOmitsContentType()
    {
        var cache = new MediaArtworkCache();
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var id = cache.Store(pngHeader, null);

        Assert.NotNull(id);
        Assert.True(cache.TryGet(id, out var artwork));
        Assert.Equal("image/png", artwork!.ContentType);
    }
}
