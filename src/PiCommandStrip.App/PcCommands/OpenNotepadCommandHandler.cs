namespace PiCommandStrip.App.PcCommands;

public sealed class OpenNotepadCommandHandler(INotepadLauncher notepadLauncher) : IPcCommandHandler
{
    public string CommandId => PcCommandIds.OpenNotepad;

    public Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.PositionMilliseconds is not null)
        {
            return Task.FromResult(PcCommandExecutionResult.Failure(
                "This command does not accept a media position."));
        }

        notepadLauncher.Launch();

        return Task.FromResult(PcCommandExecutionResult.Success("Notepad opened."));
    }
}
