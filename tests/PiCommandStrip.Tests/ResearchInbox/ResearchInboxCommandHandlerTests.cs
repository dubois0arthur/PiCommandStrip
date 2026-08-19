using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.ResearchInbox;

namespace PiCommandStrip.Tests.ResearchInbox;

public sealed class ResearchInboxCommandHandlerTests
{
    [Fact]
    public async Task BrowserSelection_IsNotPersistedUntilExplicitSaveCommand()
    {
        var browser = new StubBrowserIntegrationService
        {
            CurrentState = ConnectedState("Sensitive selected passage")
        };
        var inbox = new RecordingInboxService();
        var handler = new ResearchInboxCommandHandler(
            PcCommandIds.ResearchSaveCurrent,
            inbox,
            browser,
            new StubBrowserCommandService());

        Assert.Empty(inbox.Captures);
        browser.CurrentState = ConnectedState("A changed sensitive passage");
        Assert.Empty(inbox.Captures);

        var result = await handler.ExecuteAsync(
            new(PcCommandIds.ResearchSaveCurrent),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var capture = Assert.Single(inbox.Captures);
        Assert.Equal("A changed sensitive passage", capture.SelectedText);
    }

    [Fact]
    public async Task OpenItem_RejectsStaleIdWithoutCallingBrowser()
    {
        var browserCommands = new StubBrowserCommandService();
        var handler = new ResearchInboxCommandHandler(
            PcCommandIds.ResearchOpenItem,
            new RecordingInboxService(),
            new StubBrowserIntegrationService(),
            browserCommands);

        var result = await handler.ExecuteAsync(
            new(PcCommandIds.ResearchOpenItem, ResearchItemId: 500),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, browserCommands.OpenCount);
    }

    private static BrowserState ConnectedState(string selectedText) => new(
        BrowserConnectionStates.Connected,
        "firefox",
        "bridge",
        "instance",
        42,
        "https://example.com/research",
        "example.com",
        "Research article",
        selectedText,
        true,
        false,
        DateTimeOffset.UtcNow);

    private sealed class RecordingInboxService : IResearchInboxService
    {
        public List<ResearchCapture> Captures { get; } = [];
        public ResearchInboxState Current { get; } = new(0, 0, 0, "initialized", null, DateTimeOffset.UtcNow);

        public Task<ResearchSaveResult> SaveAsync(ResearchCapture capture, CancellationToken cancellationToken)
        {
            Captures.Add(capture);
            var item = new ResearchItem(
                1, capture.Title!, capture.Url, capture.Url, "example.com", capture.SelectedText,
                DateTimeOffset.UtcNow, capture.SourceBrowser!, false);
            return Task.FromResult(new ResearchSaveResult(item, true));
        }

        public Task<ResearchInboxPage> GetPageAsync(long? beforeId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchInboxPage([], null, false));

        public Task<ResearchItem?> GetAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<ResearchItem?>(null);

        public Task<bool> SetReviewedAsync(long id, bool isReviewed, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class StubBrowserIntegrationService : IBrowserIntegrationService
    {
        public BrowserState CurrentState { get; set; } = BrowserState.Disconnected(DateTimeOffset.UtcNow);
        public BrowserState Current => CurrentState;
        public Task BeginConnectionAsync(Guid connectionId, BrowserIdentity identity, IBrowserExtensionCommandChannel commandChannel, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ApplyObservationAsync(Guid connectionId, BrowserTabObservation observation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndConnectionAsync(Guid connectionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetBrowserContextActiveAsync(bool isActive, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BrowserExtensionCommandResult> ExecuteExtensionCommandAsync(BrowserExtensionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserExtensionCommandResult(Guid.NewGuid(), false, "unavailable"));
    }

    private sealed class StubBrowserCommandService : IBrowserCommandService
    {
        public int OpenCount { get; private set; }
        public Task<BrowserCommandResult> ExecuteAsync(string commandId, string? searchActionId, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserCommandResult(false, "Unavailable."));
        public Task<BrowserCommandResult> OpenTrustedUriAsync(Uri uri, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult(new BrowserCommandResult(true, "Opened."));
        }
    }
}
