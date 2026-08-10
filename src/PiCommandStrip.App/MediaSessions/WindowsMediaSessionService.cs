using System.Threading.Channels;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PiCommandStrip.App.MediaSessions;

public sealed class WindowsMediaSessionService(
    MediaStateStore stateStore,
    MediaStateNormalizer normalizer,
    IMediaArtworkCache artworkCache,
    IMediaStateBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<WindowsMediaSessionService> logger) : BackgroundService, IMediaSessionService
{
    private static readonly TimeSpan PlayingPositionRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ManagerRetryInterval = TimeSpan.FromSeconds(30);
    private readonly Channel<bool> _refreshRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private int _artworkRevision;
    private int _lastReadArtworkRevision = -1;
    private string? _currentArtworkId;

    public MediaState Current => stateStore.Current;

    public Task<MediaSessionCommandResult> PlayAsync(CancellationToken cancellationToken) =>
        ExecuteTransportCommandAsync(MediaTransportCommand.Play, cancellationToken);

    public Task<MediaSessionCommandResult> PauseAsync(CancellationToken cancellationToken) =>
        ExecuteTransportCommandAsync(MediaTransportCommand.Pause, cancellationToken);

    public Task<MediaSessionCommandResult> TogglePlayPauseAsync(
        CancellationToken cancellationToken) =>
        ExecuteTransportCommandAsync(MediaTransportCommand.PlayPause, cancellationToken);

    public Task<MediaSessionCommandResult> SkipPreviousAsync(
        CancellationToken cancellationToken) =>
        ExecuteTransportCommandAsync(MediaTransportCommand.Previous, cancellationToken);

    public Task<MediaSessionCommandResult> SkipNextAsync(CancellationToken cancellationToken) =>
        ExecuteTransportCommandAsync(MediaTransportCommand.Next, cancellationToken);

    public async Task<MediaSessionCommandResult> SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (position < TimeSpan.Zero)
        {
            return MediaSessionCommandResult.Failure("The requested media position is invalid.");
        }

        try
        {
            var session = GetCurrentSessionForCommand();
            if (session is null)
            {
                return NoActiveSession();
            }

            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo?.Controls.IsPlaybackPositionEnabled is not true)
            {
                return Unsupported("seeking");
            }

            var timeline = session.GetTimelineProperties();
            if (timeline is null || timeline.EndTime <= timeline.StartTime)
            {
                return MediaSessionCommandResult.Failure(
                    "The active media session did not provide a seekable timeline.");
            }

            var duration = timeline.EndTime - timeline.StartTime;
            if (position > duration)
            {
                return MediaSessionCommandResult.Failure(
                    "The requested media position is outside the active timeline.");
            }

            var requestedPosition = timeline.StartTime + position;
            var minimumSeekPosition = timeline.MinSeekTime;
            var maximumSeekPosition = timeline.MaxSeekTime;
            if (maximumSeekPosition > minimumSeekPosition &&
                (requestedPosition < minimumSeekPosition ||
                 requestedPosition > maximumSeekPosition))
            {
                return MediaSessionCommandResult.Failure(
                    "The requested media position is outside the session's seek range.");
            }

            var accepted = await session.TryChangePlaybackPositionAsync(requestedPosition.Ticks);
            cancellationToken.ThrowIfCancellationRequested();
            QueueRefresh();

            return accepted
                ? MediaSessionCommandResult.Success("Media position changed.")
                : Rejected("seek");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The active media session disappeared during a seek command");
            QueueRefresh();
            return SessionUnavailable();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ObserveManagerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Windows media-session monitoring could not start; retrying in {RetrySeconds} seconds",
                    ManagerRetryInterval.TotalSeconds);
                await PublishSnapshotAsync(null, stoppingToken);
                await Task.Delay(ManagerRetryInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task ObserveManagerAsync(CancellationToken stoppingToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        stoppingToken.ThrowIfCancellationRequested();

        Volatile.Write(ref _manager, manager);
        manager.CurrentSessionChanged += HandleCurrentSessionChanged;
        manager.SessionsChanged += HandleSessionsChanged;
        LogDiscoveredSessionCount(manager);
        QueueRefresh();

        var positionRefreshTask = QueuePlayingPositionRefreshesAsync(stoppingToken);

        try
        {
            while (await _refreshRequests.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_refreshRequests.Reader.TryRead(out _))
                {
                }

                await RefreshCurrentSessionAsync(manager, stoppingToken);
            }
        }
        finally
        {
            manager.CurrentSessionChanged -= HandleCurrentSessionChanged;
            manager.SessionsChanged -= HandleSessionsChanged;
            Interlocked.CompareExchange(ref _manager, null, manager);
            SwitchCurrentSession(null);
            try
            {
                await positionRefreshTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal host shutdown.
            }
        }
    }

    private async Task QueuePlayingPositionRefreshesAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PlayingPositionRefreshInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (Current.HasActiveSession &&
                string.Equals(
                    Current.PlaybackState,
                    MediaPlaybackStates.Playing,
                    StringComparison.Ordinal))
            {
                QueueRefresh();
            }
        }
    }

    private async Task RefreshCurrentSessionAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = manager.GetCurrentSession();
            SwitchCurrentSession(session);
            var snapshot = session is null
                ? null
                : await ReadSnapshotAsync(session, cancellationToken);
            await PublishSnapshotAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the current Windows media session");
            await PublishSnapshotAsync(null, cancellationToken);
        }
    }

    private async Task<MediaSessionSnapshot> ReadSnapshotAsync(
        GlobalSystemMediaTransportControlsSession session,
        CancellationToken cancellationToken)
    {
        string? sourceIdentifier = null;
        GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties = null;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null;
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timelineProperties = null;

        try
        {
            sourceIdentifier = session.SourceAppUserModelId;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "A media session did not expose its source identifier");
        }

        try
        {
            mediaProperties = await session.TryGetMediaPropertiesAsync();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "A media session did not expose media properties");
        }

        try
        {
            playbackInfo = session.GetPlaybackInfo();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "A media session did not expose playback information");
        }

        try
        {
            timelineProperties = session.GetTimelineProperties();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "A media session did not expose timeline properties");
        }

        var playbackStatus = MapPlaybackStatus(playbackInfo?.PlaybackStatus);
        var (position, duration) = ReadTimeline(timelineProperties, playbackStatus);
        var controls = playbackInfo?.Controls;
        var artworkId = await ReadArtworkIfChangedAsync(mediaProperties, cancellationToken);

        return new MediaSessionSnapshot(
            sourceIdentifier,
            GetSourceName(sourceIdentifier),
            mediaProperties?.Title,
            mediaProperties?.Artist,
            mediaProperties?.AlbumTitle,
            playbackStatus,
            position,
            duration,
            controls?.IsPreviousEnabled ?? false,
            controls?.IsNextEnabled ?? false,
            controls?.IsPlayEnabled ?? false,
            controls?.IsPauseEnabled ?? false,
            controls?.IsPlayPauseToggleEnabled ?? false,
            controls?.IsPlaybackPositionEnabled ?? false,
            artworkId);
    }

    private async Task<string?> ReadArtworkIfChangedAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties,
        CancellationToken cancellationToken)
    {
        var artworkRevision = Volatile.Read(ref _artworkRevision);
        if (_lastReadArtworkRevision == artworkRevision)
        {
            return _currentArtworkId;
        }

        _lastReadArtworkRevision = artworkRevision;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var thumbnailReference = mediaProperties?.Thumbnail;
            if (thumbnailReference is null)
            {
                artworkCache.Clear();
                _currentArtworkId = null;
                return null;
            }

            using var thumbnailStream = await thumbnailReference.OpenReadAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (thumbnailStream.Size is 0 or > MediaArtworkCache.MaximumArtworkBytes)
            {
                artworkCache.Clear();
                _currentArtworkId = null;
                return null;
            }

            using var inputStream = thumbnailStream.GetInputStreamAt(0);
            using var reader = new DataReader(inputStream);
            var byteCount = checked((uint)thumbnailStream.Size);
            var loadedByteCount = await reader.LoadAsync(byteCount);
            cancellationToken.ThrowIfCancellationRequested();

            if (loadedByteCount != byteCount)
            {
                artworkCache.Clear();
                _currentArtworkId = null;
                return null;
            }

            var bytes = new byte[byteCount];
            reader.ReadBytes(bytes);
            _currentArtworkId = artworkCache.Store(bytes, thumbnailStream.ContentType);
            return _currentArtworkId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The active media session did not expose usable artwork");
            artworkCache.Clear();
            _currentArtworkId = null;
            return null;
        }
    }

    private (TimeSpan? Position, TimeSpan? Duration) ReadTimeline(
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline,
        MediaPlaybackStatus playbackStatus)
    {
        if (timeline is null)
        {
            return (null, null);
        }

        TimeSpan? duration = timeline.EndTime > timeline.StartTime
            ? timeline.EndTime - timeline.StartTime
            : null;
        var position = timeline.Position - timeline.StartTime;

        if (playbackStatus is MediaPlaybackStatus.Playing)
        {
            var elapsed = timeProvider.GetUtcNow() - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
            {
                position += elapsed;
            }
        }

        return (position, duration);
    }

    private async Task PublishSnapshotAsync(
        MediaSessionSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var observation = normalizer.Normalize(snapshot, timeProvider.GetUtcNow());
        if (!stateStore.TryUpdate(observation, out var changedState))
        {
            return;
        }

        await broadcaster.BroadcastAsync(changedState, cancellationToken);
    }

    private async Task<MediaSessionCommandResult> ExecuteTransportCommandAsync(
        MediaTransportCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var session = GetCurrentSessionForCommand();
            if (session is null)
            {
                return NoActiveSession();
            }

            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo is null || !Supports(command, playbackInfo))
            {
                return Unsupported(CommandDisplayName(command));
            }

            var controls = playbackInfo.Controls;

            var accepted = command switch
            {
                MediaTransportCommand.Play => await session.TryPlayAsync(),
                MediaTransportCommand.Pause => await session.TryPauseAsync(),
                MediaTransportCommand.PlayPause when controls.IsPlayPauseToggleEnabled =>
                    await session.TryTogglePlayPauseAsync(),
                MediaTransportCommand.PlayPause when playbackInfo!.PlaybackStatus is
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing =>
                    await session.TryPauseAsync(),
                MediaTransportCommand.PlayPause => await session.TryPlayAsync(),
                MediaTransportCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaTransportCommand.Next => await session.TrySkipNextAsync(),
                _ => false
            };

            cancellationToken.ThrowIfCancellationRequested();
            QueueRefresh();

            return accepted
                ? MediaSessionCommandResult.Success(SuccessMessage(command))
                : Rejected(CommandDisplayName(command));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The active media session disappeared during command {MediaCommand}",
                command);
            QueueRefresh();
            return SessionUnavailable();
        }
    }

    private GlobalSystemMediaTransportControlsSession? GetCurrentSessionForCommand() =>
        Volatile.Read(ref _manager)?.GetCurrentSession();

    private static bool Supports(
        MediaTransportCommand command,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
    {
        var controls = playbackInfo.Controls;
        return command switch
        {
            MediaTransportCommand.Play => controls.IsPlayEnabled,
            MediaTransportCommand.Pause => controls.IsPauseEnabled,
            MediaTransportCommand.PlayPause =>
                controls.IsPlayPauseToggleEnabled ||
                (playbackInfo.PlaybackStatus is
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                        ? controls.IsPauseEnabled
                        : controls.IsPlayEnabled),
            MediaTransportCommand.Previous => controls.IsPreviousEnabled,
            MediaTransportCommand.Next => controls.IsNextEnabled,
            _ => false
        };
    }

    private static string CommandDisplayName(MediaTransportCommand command) => command switch
    {
        MediaTransportCommand.Play => "play",
        MediaTransportCommand.Pause => "pause",
        MediaTransportCommand.PlayPause => "play/pause",
        MediaTransportCommand.Previous => "previous",
        MediaTransportCommand.Next => "next",
        _ => "media control"
    };

    private static string SuccessMessage(MediaTransportCommand command) => command switch
    {
        MediaTransportCommand.Play => "Media playback started.",
        MediaTransportCommand.Pause => "Media playback paused.",
        MediaTransportCommand.PlayPause => "Media playback toggled.",
        MediaTransportCommand.Previous => "Previous media requested.",
        MediaTransportCommand.Next => "Next media requested.",
        _ => "Media command completed."
    };

    private static MediaSessionCommandResult NoActiveSession() =>
        MediaSessionCommandResult.Failure("No active media session is available.");

    private static MediaSessionCommandResult Unsupported(string operation) =>
        MediaSessionCommandResult.Failure(
            $"The active media session does not support {operation}.");

    private static MediaSessionCommandResult Rejected(string operation) =>
        MediaSessionCommandResult.Failure(
            $"The active media session rejected the {operation} request.");

    private static MediaSessionCommandResult SessionUnavailable() =>
        MediaSessionCommandResult.Failure("The active media session is no longer available.");

    private void SwitchCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_currentSession, session))
        {
            return;
        }

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= HandleMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= HandlePlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged -= HandleTimelinePropertiesChanged;
        }

        _currentSession = session;

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged += HandleMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += HandlePlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += HandleTimelinePropertiesChanged;
        }

        artworkCache.Clear();
        _currentArtworkId = null;
        Interlocked.Increment(ref _artworkRevision);
    }

    private void HandleCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) =>
        QueueRefresh();

    private void HandleSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        LogDiscoveredSessionCount(sender);
        QueueRefresh();
    }

    private void HandleMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        Interlocked.Increment(ref _artworkRevision);
        QueueRefresh();
    }

    private void HandlePlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) =>
        QueueRefresh();

    private void HandleTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) =>
        QueueRefresh();

    private void QueueRefresh() => _refreshRequests.Writer.TryWrite(true);

    private void LogDiscoveredSessionCount(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            logger.LogDebug(
                "Discovered {MediaSessionCount} Windows system media sessions",
                manager.GetSessions().Count);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not enumerate Windows system media sessions");
        }
    }

    private static MediaPlaybackStatus MapPlaybackStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackStatus.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
            _ => MediaPlaybackStatus.Unknown
        };

    private static string? GetSourceName(string? sourceIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentifier))
        {
            return null;
        }

        var sourceName = sourceIdentifier.Trim();
        var appSeparator = sourceName.LastIndexOf('!');
        if (appSeparator >= 0 && appSeparator < sourceName.Length - 1)
        {
            sourceName = sourceName[(appSeparator + 1)..];
        }

        return Path.GetFileNameWithoutExtension(sourceName);
    }

    private enum MediaTransportCommand
    {
        Play,
        Pause,
        PlayPause,
        Previous,
        Next
    }
}
