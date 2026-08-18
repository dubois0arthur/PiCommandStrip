using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.App.PcCommands;

public sealed class SpotifyCommandHandler(
    string commandId,
    ISpotifyService spotifyService) : IPcCommandHandler
{
    public string CommandId { get; } = commandId;

    public async Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = CommandId switch
        {
            PcCommandIds.SpotifySetSaved when invocation.IsSaved is bool isSaved =>
                await spotifyService.SetSavedAsync(isSaved, cancellationToken),
            PcCommandIds.SpotifySetShuffle when invocation.ShuffleEnabled is bool shuffle =>
                await spotifyService.SetShuffleAsync(shuffle, cancellationToken),
            PcCommandIds.SpotifySetRepeat when SpotifyRepeatStates.IsValid(invocation.RepeatState) =>
                await spotifyService.SetRepeatAsync(invocation.RepeatState!, cancellationToken),
            _ => SpotifyCommandResult.Failure("Invalid Spotify command parameters.")
        };

        return result.Succeeded
            ? PcCommandExecutionResult.Success(result.Message)
            : PcCommandExecutionResult.Failure(result.Message);
    }
}
