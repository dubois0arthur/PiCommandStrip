namespace PiCommandStrip.App.PcCommands;

public interface IPcCommandHandler
{
    string CommandId { get; }

    Task<PcCommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken);
}
