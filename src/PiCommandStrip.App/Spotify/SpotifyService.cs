using System.Net;
using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.Spotify;

public sealed class SpotifyService(
    SpotifyConfiguration configuration,
    SpotifyStateStore stateStore,
    SpotifyTokenManager tokenManager,
    ISpotifyApiClient apiClient,
    IMediaSessionService mediaSessionService,
    ISpotifyStateBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<SpotifyService> logger) : BackgroundService, ISpotifyService
{
    private static readonly TimeSpan ActiveRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AuxiliaryRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureRefreshInterval = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private DateTimeOffset _lastAuxiliaryRefreshUtc;

    public SpotifyState Current => stateStore.Current;

    public Task<SpotifyCommandResult> SetSavedAsync(
        bool isSaved,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            async (accessToken, itemUri, token) =>
            {
                await apiClient.SetSavedAsync(accessToken, itemUri, isSaved, token);
                return Current with
                {
                    Status = SpotifyStatuses.Available,
                    IsSaved = isSaved,
                    LastUpdatedUtc = timeProvider.GetUtcNow(),
                    RetryAfterUtc = null
                };
            },
            isSaved ? "Saved current Spotify item." : "Removed current Spotify item from your library.",
            cancellationToken);

    public Task<SpotifyCommandResult> SetShuffleAsync(
        bool enabled,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            async (accessToken, _, token) =>
            {
                await apiClient.SetShuffleAsync(accessToken, enabled, token);
                return Current with
                {
                    Status = SpotifyStatuses.Available,
                    ShuffleEnabled = enabled,
                    LastUpdatedUtc = timeProvider.GetUtcNow(),
                    RetryAfterUtc = null
                };
            },
            enabled ? "Spotify shuffle enabled." : "Spotify shuffle disabled.",
            cancellationToken);

    public Task<SpotifyCommandResult> SetRepeatAsync(
        string repeatState,
        CancellationToken cancellationToken)
    {
        if (!SpotifyRepeatStates.IsValid(repeatState))
        {
            return Task.FromResult(SpotifyCommandResult.Failure("Invalid Spotify repeat state."));
        }

        return ExecuteCommandAsync(
            async (accessToken, _, token) =>
            {
                await apiClient.SetRepeatAsync(accessToken, repeatState, token);
                return Current with
                {
                    Status = SpotifyStatuses.Available,
                    RepeatState = repeatState,
                    LastUpdatedUtc = timeProvider.GetUtcNow(),
                    RetryAfterUtc = null
                };
            },
            $"Spotify repeat set to {repeatState}.",
            cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await RefreshSafelyAsync(stoppingToken);
            await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }

    private async Task<TimeSpan> RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SpotifyApiException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = exception.RetryAfter ?? FailureRefreshInterval;
            var now = timeProvider.GetUtcNow();
            logger.LogWarning(
                "Spotify API rate limited operation {SpotifyOperation}; retrying after {RetryAfterSeconds} seconds",
                exception.Operation,
                Math.Ceiling(retryAfter.TotalSeconds));
            await PublishAsync(
                SpotifyStateFactory.Failure(
                    mediaSessionService.Current,
                    Current,
                    SpotifyStatuses.RateLimited,
                    now,
                    now + retryAfter),
                cancellationToken);
            return retryAfter;
        }
        catch (SpotifyApiException exception)
        {
            logger.LogWarning(
                "Spotify API operation {SpotifyOperation} failed with HTTP status {SpotifyStatusCode}",
                exception.Operation,
                (int)exception.StatusCode);
            await PublishAsync(
                SpotifyStateFactory.Failure(
                    mediaSessionService.Current,
                    Current,
                    SpotifyStatuses.Error,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            return FailureRefreshInterval;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Spotify enrichment refresh failed");
            await PublishAsync(
                SpotifyStateFactory.Failure(
                    mediaSessionService.Current,
                    Current,
                    SpotifyStatuses.Error,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            return FailureRefreshInterval;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<TimeSpan> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!configuration.IsConfigured)
        {
            await PublishAsync(SpotifyState.Unconfigured(now), cancellationToken);
            return FailureRefreshInterval;
        }

        var mediaState = mediaSessionService.Current;
        var hasAuthorization = await tokenManager.HasAuthorizationAsync(cancellationToken);
        if (!SpotifyMediaMatcher.IsSpotifySource(mediaState))
        {
            await PublishAsync(SpotifyState.Idle(hasAuthorization, now), cancellationToken);
            return IdleRefreshInterval;
        }

        var accessToken = await tokenManager.GetAccessTokenAsync(cancellationToken);
        if (accessToken is null)
        {
            await PublishAsync(SpotifyState.Unauthenticated(now), cancellationToken);
            return FailureRefreshInterval;
        }

        var playback = await ExecuteWithOneTokenRefreshAsync(
            (token, cancellation) => apiClient.GetPlaybackAsync(token, cancellation),
            accessToken,
            cancellationToken);
        if (playback is null)
        {
            await PublishAsync(SpotifyState.Idle(true, now), cancellationToken);
            return ActiveRefreshInterval;
        }

        var applies = SpotifyMediaMatcher.MatchesCurrentItem(mediaState, playback);
        if (!applies)
        {
            await PublishAsync(
                SpotifyStateFactory.Available(mediaState, playback, null, [], now),
                cancellationToken);
            return ActiveRefreshInterval;
        }

        var itemChanged = !string.Equals(
            Current.ItemUri,
            playback.ItemUri,
            StringComparison.Ordinal);
        var refreshAuxiliary = itemChanged ||
            now - _lastAuxiliaryRefreshUtc >= AuxiliaryRefreshInterval;
        var saved = itemChanged ? null : Current.IsSaved;
        IReadOnlyList<SpotifyQueueItemState> queue = itemChanged ? [] : Current.Queue;
        if (refreshAuxiliary && playback.ItemUri is not null)
        {
            (saved, queue) = await ReadAuxiliaryStateAsync(
                accessToken,
                playback.ItemUri,
                saved,
                queue,
                cancellationToken);
            _lastAuxiliaryRefreshUtc = now;
        }

        await PublishAsync(
            SpotifyStateFactory.Available(mediaState, playback, saved, queue, now),
            cancellationToken);
        return ActiveRefreshInterval;
    }

    private async Task<(bool? Saved, IReadOnlyList<SpotifyQueueItemState> Queue)>
        ReadAuxiliaryStateAsync(
            string accessToken,
            string itemUri,
            bool? previousSaved,
            IReadOnlyList<SpotifyQueueItemState> previousQueue,
            CancellationToken cancellationToken)
    {
        var saved = previousSaved;
        IReadOnlyList<SpotifyQueueItemState> queue = previousQueue;

        try
        {
            saved = await ExecuteWithOneTokenRefreshAsync(
                (token, cancellation) => apiClient.IsSavedAsync(token, itemUri, cancellation),
                accessToken,
                cancellationToken);
        }
        catch (SpotifyApiException exception) when (exception.StatusCode != HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "Spotify saved-state enrichment failed with HTTP status {SpotifyStatusCode}",
                (int)exception.StatusCode);
        }

        try
        {
            var snapshot = await ExecuteWithOneTokenRefreshAsync(
                (token, cancellation) => apiClient.GetQueueAsync(token, cancellation),
                accessToken,
                cancellationToken);
            queue = snapshot.Items;
        }
        catch (SpotifyApiException exception) when (exception.StatusCode != HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "Spotify queue enrichment failed with HTTP status {SpotifyStatusCode}",
                (int)exception.StatusCode);
        }

        return (saved, queue);
    }

    private async Task<T> ExecuteWithOneTokenRefreshAsync<T>(
        Func<string, CancellationToken, Task<T>> operation,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(accessToken, cancellationToken);
        }
        catch (SpotifyApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenManager.InvalidateAccessToken();
            var refreshed = await tokenManager.GetAccessTokenAsync(cancellationToken);
            if (refreshed is null)
            {
                throw;
            }

            return await operation(refreshed, cancellationToken);
        }
    }

    private async Task<SpotifyCommandResult> ExecuteCommandAsync(
        Func<string, string, CancellationToken, Task<SpotifyState>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var state = Current;
            var media = mediaSessionService.Current;
            if (state.Status != SpotifyStatuses.Available ||
                !state.AppliesToCurrentMedia ||
                string.IsNullOrWhiteSpace(state.ItemUri) ||
                !SpotifyMediaMatcher.IsSpotifySource(media) ||
                !string.Equals(media.Title, state.MatchedMediaTitle, StringComparison.Ordinal))
            {
                return SpotifyCommandResult.Failure("No confidently matched Spotify item is active.");
            }

            if (state.Device?.IsRestricted == true)
            {
                return SpotifyCommandResult.Failure("The current Spotify device does not accept Web API controls.");
            }

            var accessToken = await tokenManager.GetAccessTokenAsync(cancellationToken);
            if (accessToken is null)
            {
                await PublishAsync(
                    SpotifyState.Unauthenticated(timeProvider.GetUtcNow()),
                    cancellationToken);
                return SpotifyCommandResult.Failure("Spotify authorization is required.");
            }

            SpotifyState changed;
            try
            {
                changed = await operation(accessToken, state.ItemUri, cancellationToken);
            }
            catch (SpotifyApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                tokenManager.InvalidateAccessToken();
                var refreshed = await tokenManager.GetAccessTokenAsync(cancellationToken);
                if (refreshed is null)
                {
                    await PublishAsync(
                        SpotifyState.Unauthenticated(timeProvider.GetUtcNow()),
                        cancellationToken);
                    return SpotifyCommandResult.Failure("Spotify authorization is required.");
                }

                changed = await operation(refreshed, state.ItemUri, cancellationToken);
            }
            await PublishAsync(changed, cancellationToken);
            return SpotifyCommandResult.Success(successMessage);
        }
        catch (SpotifyApiException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = exception.RetryAfter ?? FailureRefreshInterval;
            var now = timeProvider.GetUtcNow();
            await PublishAsync(
                SpotifyStateFactory.Failure(
                    mediaSessionService.Current,
                    Current,
                    SpotifyStatuses.RateLimited,
                    now,
                    now + retryAfter),
                cancellationToken);
            return SpotifyCommandResult.Failure("Spotify is rate limited. Try again shortly.");
        }
        catch (SpotifyApiException exception)
        {
            logger.LogWarning(
                "Spotify command operation {SpotifyOperation} failed with HTTP status {SpotifyStatusCode}",
                exception.Operation,
                (int)exception.StatusCode);
            await PublishAsync(
                SpotifyStateFactory.Failure(
                    mediaSessionService.Current,
                    Current,
                    SpotifyStatuses.Error,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            return SpotifyCommandResult.Failure("Spotify enrichment is temporarily unavailable.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task PublishAsync(
        SpotifyState observation,
        CancellationToken cancellationToken)
    {
        if (stateStore.TryUpdate(observation, out var changed))
        {
            await broadcaster.BroadcastAsync(changed, cancellationToken);
        }
    }
}
