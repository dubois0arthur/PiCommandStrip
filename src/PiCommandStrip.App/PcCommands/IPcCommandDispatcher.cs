namespace PiCommandStrip.App.PcCommands;

public interface IPcCommandDispatcher
{
    Task<PcCommandExecutionResult> DispatchAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken);
}
