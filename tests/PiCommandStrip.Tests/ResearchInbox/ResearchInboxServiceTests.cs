using Microsoft.Extensions.Logging.Abstractions;
using PiCommandStrip.App.ResearchInbox;

namespace PiCommandStrip.Tests.ResearchInbox;

public sealed class ResearchInboxServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PiCommandStrip.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly RecordingBroadcaster _broadcaster = new();

    [Fact]
    public async Task SavePage_PersistsNormalizedPageWithoutSelection()
    {
        var service = CreateService();

        var result = await service.SaveAsync(
            new("An article", "HTTPS://Example.COM:443/path?q=1#section", null, "firefox"),
            CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.Equal("https://example.com/path?q=1", result.Item.NormalizedUrl);
        Assert.Equal("example.com", result.Item.Domain);
        Assert.Null(result.Item.SelectedText);
        Assert.False(result.Item.IsReviewed);
        Assert.Equal(1, service.Current.TotalCount);
    }

    [Fact]
    public async Task SaveSelectedPassage_PersistsOnlyTheExplicitCapture()
    {
        var service = CreateService();

        var result = await service.SaveAsync(
            new("Article", "https://example.com/article", "  Useful\r\npassage  ", "firefox"),
            CancellationToken.None);

        Assert.Equal("Useful\npassage", result.Item.SelectedText);
    }

    [Fact]
    public async Task SamePageWithoutSelection_IsDeduplicated()
    {
        var service = CreateService();
        var first = await service.SaveAsync(
            new("First title", "https://example.com/page#first", null, "firefox"),
            CancellationToken.None);
        var duplicate = await service.SaveAsync(
            new("Changed title", "https://EXAMPLE.com:443/page#second", "  ", "firefox"),
            CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(duplicate.WasCreated);
        Assert.Equal(first.Item.Id, duplicate.Item.Id);
        Assert.Equal("First title", duplicate.Item.Title);
        Assert.Equal(1, service.Current.TotalCount);
        Assert.Single(_broadcaster.States);
    }

    [Fact]
    public async Task SamePageWithDistinctSelections_PreservesEachPassage()
    {
        var service = CreateService();
        var first = await service.SaveAsync(
            new("Article", "https://example.com/page", "First passage", "firefox"),
            CancellationToken.None);
        var second = await service.SaveAsync(
            new("Article", "https://example.com/page", "Second passage", "firefox"),
            CancellationToken.None);
        var duplicate = await service.SaveAsync(
            new("Article", "https://example.com/page#anchor", " First passage ", "firefox"),
            CancellationToken.None);

        Assert.NotEqual(first.Item.Id, second.Item.Id);
        Assert.False(duplicate.WasCreated);
        Assert.Equal(first.Item.Id, duplicate.Item.Id);
        Assert.Equal(2, service.Current.TotalCount);
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("not a URL")]
    public async Task InvalidUrl_IsRejected(string url)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ResearchInboxValidationException>(() =>
            service.SaveAsync(
                new("Invalid", url, null, "firefox"),
                CancellationToken.None));
    }

    [Fact]
    public async Task MarkReviewed_UpdatesExistingItemAndRejectsStaleId()
    {
        var service = CreateService();
        var saved = await service.SaveAsync(
            new("Article", "https://example.com", null, "firefox"),
            CancellationToken.None);

        Assert.True(await service.SetReviewedAsync(
            saved.Item.Id, true, CancellationToken.None));
        Assert.True((await service.GetAsync(
            saved.Item.Id, CancellationToken.None))!.IsReviewed);
        Assert.False(await service.SetReviewedAsync(
            999_999, true, CancellationToken.None));
        Assert.Equal(0, service.Current.UnreviewedCount);
    }

    [Fact]
    public async Task Delete_RemovesItemAndReturnsFalseForStaleId()
    {
        var service = CreateService();
        var saved = await service.SaveAsync(
            new("Article", "https://example.com", null, "firefox"),
            CancellationToken.None);

        Assert.True(await service.DeleteAsync(saved.Item.Id, CancellationToken.None));
        Assert.Null(await service.GetAsync(saved.Item.Id, CancellationToken.None));
        Assert.False(await service.DeleteAsync(saved.Item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetPage_IsBoundedAndUsesExclusiveCursor()
    {
        var service = CreateService();
        for (var index = 0; index < 5; index++)
        {
            await service.SaveAsync(
                new($"Item {index}", $"https://example.com/{index}", null, "firefox"),
            CancellationToken.None);
        }

        var first = await service.GetPageAsync(null, 2, CancellationToken.None);
        var second = await service.GetPageAsync(
            first.NextBeforeId, 2, CancellationToken.None);
        var third = await service.GetPageAsync(
            second.NextBeforeId, 2, CancellationToken.None);

        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(2, second.Items.Count);
        Assert.True(second.HasMore);
        Assert.Single(third.Items);
        Assert.False(third.HasMore);
        Assert.Equal(5, first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).Distinct().Count());
    }

    private SqliteResearchInboxService CreateService()
    {
        Directory.CreateDirectory(_directory);
        return new(
            Path.Combine(_directory, "inbox.db"),
            TimeProvider.System,
            _broadcaster,
            NullLogger<SqliteResearchInboxService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingBroadcaster : IResearchInboxStateBroadcaster
    {
        public List<ResearchInboxState> States { get; } = [];

        public Task BroadcastAsync(ResearchInboxState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            return Task.CompletedTask;
        }
    }
}
