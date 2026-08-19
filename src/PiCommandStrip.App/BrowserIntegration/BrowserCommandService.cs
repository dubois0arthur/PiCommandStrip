using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserCommandService(
    IBrowserIntegrationService integrationService,
    BrowserSearchCatalog searchCatalog) : IBrowserCommandService
{
    public async Task<BrowserCommandResult> ExecuteAsync(
        string commandId,
        string? searchActionId,
        CancellationToken cancellationToken)
    {
        var state = integrationService.Current;
        if (!state.IsConnected)
        {
            return Failure("Browser integration is unavailable.");
        }

        var requiresTab = commandId is not (
            PcCommandIds.BrowserNewTab or PcCommandIds.BrowserReopenClosedTab);
        if (requiresTab && state.ActiveTabId is null)
        {
            return Failure("No active browser tab is available.");
        }

        if (commandId == PcCommandIds.BrowserBack && state.CanGoBack is not true)
        {
            return Failure("Back is unavailable for the current tab.");
        }

        if (commandId == PcCommandIds.BrowserForward && state.CanGoForward is not true)
        {
            return Failure("Forward is unavailable for the current tab.");
        }

        string? searchUrl = null;
        if (commandId == PcCommandIds.BrowserSearchSelection)
        {
            if (!searchCatalog.TryBuildUrl(searchActionId, state.SelectedText, out var url))
            {
                return Failure("The selected-text search is unavailable.");
            }
            searchUrl = url!.AbsoluteUri;
        }
        else if (searchActionId is not null)
        {
            return Failure("This browser action does not accept a search provider.");
        }

        if (commandId == PcCommandIds.BrowserCopyCurrentUrl &&
            (!Uri.TryCreate(state.Url, UriKind.Absolute, out var currentUrl) ||
             currentUrl.Scheme is not ("http" or "https")))
        {
            return Failure("The current page URL cannot be copied.");
        }

        var result = await integrationService.ExecuteExtensionCommandAsync(
            new BrowserExtensionCommand(commandId, requiresTab ? state.ActiveTabId : null, searchUrl),
            cancellationToken);
        return result.Succeeded
            ? new BrowserCommandResult(true, SuccessMessage(commandId, searchActionId))
            : Failure(FailureMessage(result.Code));
    }

    private static string SuccessMessage(string commandId, string? searchActionId) => commandId switch
    {
        PcCommandIds.BrowserBack => "Navigated back.",
        PcCommandIds.BrowserForward => "Navigated forward.",
        PcCommandIds.BrowserReload => "Page reloaded.",
        PcCommandIds.BrowserNewTab => "New tab opened.",
        PcCommandIds.BrowserCloseTab => "Current tab closed.",
        PcCommandIds.BrowserReopenClosedTab => "Last closed tab reopened.",
        PcCommandIds.BrowserCopyCurrentUrl => "Current URL copied.",
        PcCommandIds.BrowserSearchSelection => $"Selection opened with {searchActionId}.",
        _ => "Browser action completed."
    };

    private static string FailureMessage(string code) => code switch
    {
        "stale_tab" => "The active tab changed before the action completed.",
        "no_closed_tab" => "No recently closed tab is available.",
        "clipboard_failed" => "Firefox could not copy the current URL.",
        "command_timeout" => "Firefox did not answer the browser action in time.",
        "bridge_disconnected" => "Browser integration disconnected during the action.",
        _ => "Firefox could not complete the browser action."
    };

    private static BrowserCommandResult Failure(string message) => new(false, message);
}
