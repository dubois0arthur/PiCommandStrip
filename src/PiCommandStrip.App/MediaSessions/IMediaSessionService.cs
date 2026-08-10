namespace PiCommandStrip.App.MediaSessions;

public interface IMediaSessionService
{
    MediaState Current { get; }

    Task<MediaSessionCommandResult> PlayAsync(CancellationToken cancellationToken);

    Task<MediaSessionCommandResult> PauseAsync(CancellationToken cancellationToken);

    Task<MediaSessionCommandResult> TogglePlayPauseAsync(CancellationToken cancellationToken);

    Task<MediaSessionCommandResult> SkipPreviousAsync(CancellationToken cancellationToken);

    Task<MediaSessionCommandResult> SkipNextAsync(CancellationToken cancellationToken);

    Task<MediaSessionCommandResult> SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken);
}

public interface IMediaStateBroadcaster
{
    Task BroadcastAsync(MediaState state, CancellationToken cancellationToken);
}
