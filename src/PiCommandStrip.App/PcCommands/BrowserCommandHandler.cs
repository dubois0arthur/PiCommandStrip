using PiCommandStrip.App.BrowserIntegration;

namespace PiCommandStrip.App.PcCommands;

public sealed class BrowserCommandHandler(
    string commandId,
    IBrowserCommandService browserCommandService) : IPcCommandHandler
{
    public string CommandId { get; } = commandId;

    public async Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = await browserCommandService.ExecuteAsync(
            invocation.CommandId,
            invocation.SearchActionId,
            cancellationToken);
        return result.Succeeded
            ? PcCommandExecutionResult.Success(result.Message)
            : PcCommandExecutionResult.Failure(result.Message);
    }
}
