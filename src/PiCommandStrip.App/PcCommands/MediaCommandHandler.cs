using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.PcCommands;

public sealed class MediaCommandHandler(
    string commandId,
    IMediaSessionService mediaSessionService) : IPcCommandHandler
{
    public static readonly long MaximumSeekPositionMilliseconds =
        TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

    public string CommandId { get; } = commandId;

    public async Task<PcCommandExecutionResult> ExecuteAsync(
        PcCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = CommandId switch
        {
            PcCommandIds.MediaPlay =>
                await mediaSessionService.PlayAsync(cancellationToken),
            PcCommandIds.MediaPause =>
                await mediaSessionService.PauseAsync(cancellationToken),
            PcCommandIds.MediaPlayPause =>
                await mediaSessionService.TogglePlayPauseAsync(cancellationToken),
            PcCommandIds.MediaPrevious =>
                await mediaSessionService.SkipPreviousAsync(cancellationToken),
            PcCommandIds.MediaNext =>
                await mediaSessionService.SkipNextAsync(cancellationToken),
            PcCommandIds.MediaSeek =>
                await SeekAsync(invocation.PositionMilliseconds, cancellationToken),
            _ => MediaSessionCommandResult.Failure("The media command is not supported.")
        };

        return new PcCommandExecutionResult(result.Succeeded, result.Message);
    }

    private Task<MediaSessionCommandResult> SeekAsync(
        long? positionMilliseconds,
        CancellationToken cancellationToken)
    {
        if (positionMilliseconds is null ||
            positionMilliseconds < 0 ||
            positionMilliseconds > MaximumSeekPositionMilliseconds)
        {
            return Task.FromResult(MediaSessionCommandResult.Failure(
                "The requested media position is invalid."));
        }

        return mediaSessionService.SeekAsync(
            TimeSpan.FromMilliseconds(positionMilliseconds.Value),
            cancellationToken);
    }
}
