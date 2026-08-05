namespace PiCommandStrip.App.PcCommands;

public interface IPcCommandDispatcher
{
    Task<PcCommandExecutionResult> DispatchAsync(
        string commandId,
        CancellationToken cancellationToken);
}
