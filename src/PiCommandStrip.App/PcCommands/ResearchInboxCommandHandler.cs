using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.ResearchInbox;

namespace PiCommandStrip.App.PcCommands;

public sealed class ResearchInboxCommandHandler(
    string commandId,
    IResearchInboxService inboxService,
    IBrowserIntegrationService browserIntegrationService,
    IBrowserCommandService browserCommandService) : IPcCommandHandler
{
    public string CommandId { get; } = commandId;

    public async Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.CommandId != CommandId)
        {
            return PcCommandExecutionResult.Failure("The research command is invalid.");
        }

        if (CommandId == PcCommandIds.ResearchSaveCurrent)
        {
            return await SaveCurrentAsync(cancellationToken);
        }
        if (CommandId == PcCommandIds.ResearchOpenItem)
        {
            return await OpenItemAsync(invocation.ResearchItemId, cancellationToken);
        }
        return PcCommandExecutionResult.Failure("The research command is unavailable.");
    }

    private async Task<PcCommandExecutionResult> SaveCurrentAsync(CancellationToken cancellationToken)
    {
        var state = browserIntegrationService.Current;
        if (!state.IsConnected || string.IsNullOrWhiteSpace(state.Url))
        {
            return PcCommandExecutionResult.Failure("No connected browser page is available to save.");
        }

        try
        {
            // Selected text is read only in direct response to this explicit command.
            var result = await inboxService.SaveAsync(
                new ResearchCapture(
                    state.PageTitle,
                    state.Url,
                    state.SelectedText,
                    state.BrowserType),
                cancellationToken);
            return PcCommandExecutionResult.Success(result.WasCreated
                ? "Saved to Research Inbox ✓"
                : "Already in Research Inbox.");
        }
        catch (ResearchInboxValidationException exception)
        {
            return PcCommandExecutionResult.Failure(exception.Message);
        }
    }

    private async Task<PcCommandExecutionResult> OpenItemAsync(
        long? itemId,
        CancellationToken cancellationToken)
    {
        if (itemId is null or <= 0)
        {
            return PcCommandExecutionResult.Failure("A valid Research Inbox item is required.");
        }
        var item = await inboxService.GetAsync(itemId.Value, cancellationToken);
        if (item is null)
        {
            return PcCommandExecutionResult.Failure("The Research Inbox item no longer exists.");
        }
        if (!ResearchUrlNormalizer.TryNormalize(item.Url, out var uri, out _))
        {
            return PcCommandExecutionResult.Failure("The saved page URL is no longer valid.");
        }

        var result = await browserCommandService.OpenTrustedUriAsync(uri!, cancellationToken);
        return result.Succeeded
            ? PcCommandExecutionResult.Success(result.Message)
            : PcCommandExecutionResult.Failure(result.Message);
    }
}
