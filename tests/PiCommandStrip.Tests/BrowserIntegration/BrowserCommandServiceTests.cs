using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.Tests.BrowserIntegration;

public sealed class BrowserCommandServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SearchCatalog_EncodesSelection_AndRejectsEmptyOrUnknownActions()
    {
        var catalog = CreateCatalog();

        Assert.True(catalog.TryBuildUrl("google", "C# async & await/UTF-8 ✓", out var url));
        Assert.Equal(
            "https://www.google.com/search?q=C%23%20async%20%26%20await%2FUTF-8%20%E2%9C%93",
            url?.AbsoluteUri);
        Assert.False(catalog.TryBuildUrl("google", "  ", out _));
        Assert.False(catalog.TryBuildUrl("arbitrary", "research", out _));
    }

    [Fact]
    public void SearchCatalog_RejectsUnsafeOrMalformedTemplates()
    {
        Assert.Throws<InvalidOperationException>(() => new BrowserSearchCatalog(
            Options("unsafe", "Unsafe", "http://example.com/?q={query}")));
        Assert.Throws<InvalidOperationException>(() => new BrowserSearchCatalog(
            Options("duplicate", "Duplicate", "https://example.com/{query}/{query}")));
    }

    [Fact]
    public async Task Navigation_RespectsCapabilityState_AndCarriesExpectedTab()
    {
        var integration = new StubIntegration(ConnectedState() with { CanGoBack = false });
        var service = new BrowserCommandService(integration, CreateCatalog());

        var disabled = await service.ExecuteAsync(
            PcCommandIds.BrowserBack,
            null,
            CancellationToken.None);

        Assert.False(disabled.Succeeded);
        Assert.Null(integration.LastCommand);

        integration.CurrentState = integration.CurrentState with { CanGoBack = true };
        var enabled = await service.ExecuteAsync(
            PcCommandIds.BrowserBack,
            null,
            CancellationToken.None);

        Assert.True(enabled.Succeeded);
        Assert.Equal(42, integration.LastCommand?.ExpectedActiveTabId);
    }

    [Fact]
    public async Task Search_UsesRetainedSelectionAndConfiguredUrl_NotClientUrl()
    {
        var integration = new StubIntegration(ConnectedState() with
        {
            SelectedText = "safe query & notes"
        });
        var service = new BrowserCommandService(integration, CreateCatalog());

        var result = await service.ExecuteAsync(
            PcCommandIds.BrowserSearchSelection,
            "google",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "https://www.google.com/search?q=safe%20query%20%26%20notes",
            integration.LastCommand?.SearchUrl);
        Assert.Equal(42, integration.LastCommand?.ExpectedActiveTabId);
    }

    [Fact]
    public async Task Disconnected_EmptySelection_AndStaleTabFailSafely()
    {
        var disconnected = new StubIntegration(BrowserState.Disconnected(Now));
        var disconnectedService = new BrowserCommandService(disconnected, CreateCatalog());
        Assert.False((await disconnectedService.ExecuteAsync(
            PcCommandIds.BrowserNewTab,
            null,
            CancellationToken.None)).Succeeded);

        var emptySelection = new StubIntegration(ConnectedState());
        var selectionService = new BrowserCommandService(emptySelection, CreateCatalog());
        Assert.False((await selectionService.ExecuteAsync(
            PcCommandIds.BrowserSearchSelection,
            "google",
            CancellationToken.None)).Succeeded);

        var stale = new StubIntegration(ConnectedState())
        {
            ExtensionResult = new BrowserExtensionCommandResult(Guid.NewGuid(), false, "stale_tab")
        };
        var staleService = new BrowserCommandService(stale, CreateCatalog());
        var staleResult = await staleService.ExecuteAsync(
            PcCommandIds.BrowserReload,
            null,
            CancellationToken.None);
        Assert.False(staleResult.Succeeded);
        Assert.Contains("active tab changed", staleResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BrowserSearchCatalog CreateCatalog() => new(
        Options("google", "Google", "https://www.google.com/search?q={query}"));

    private static BrowserIntegrationOptions Options(
        string id,
        string displayName,
        string template) => new()
    {
        SearchActions = new Dictionary<string, BrowserSearchActionOptions>
        {
            [id] = new() { DisplayName = displayName, UrlTemplate = template }
        }
    };

    private static BrowserState ConnectedState() => new(
        BrowserConnectionStates.Connected,
        "firefox",
        "extension",
        "instance",
        42,
        "https://example.com/research",
        "example.com",
        "Research",
        null,
        true,
        false,
        Now);

    private sealed class StubIntegration(BrowserState state) : IBrowserIntegrationService
    {
        public BrowserState Current => CurrentState;
        public BrowserState CurrentState { get; set; } = state;
        public BrowserExtensionCommand? LastCommand { get; private set; }
        public BrowserExtensionCommandResult ExtensionResult { get; set; } =
            new(Guid.NewGuid(), true, "ok");

        public Task<BrowserExtensionCommandResult> ExecuteExtensionCommandAsync(
            BrowserExtensionCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(ExtensionResult);
        }

        public Task BeginConnectionAsync(
            Guid connectionId,
            BrowserIdentity identity,
            IBrowserExtensionCommandChannel commandChannel,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ApplyObservationAsync(Guid connectionId, BrowserTabObservation observation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndConnectionAsync(Guid connectionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetBrowserContextActiveAsync(bool isActive, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
