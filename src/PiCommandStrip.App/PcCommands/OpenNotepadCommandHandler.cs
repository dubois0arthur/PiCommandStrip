namespace PiCommandStrip.App.PcCommands;

public sealed class OpenNotepadCommandHandler(INotepadLauncher notepadLauncher) : IPcCommandHandler
{
    public string CommandId => PcCommandIds.OpenNotepad;

    public Task<PcCommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        notepadLauncher.Launch();

        return Task.FromResult(PcCommandExecutionResult.Success("Notepad opened."));
    }
}
