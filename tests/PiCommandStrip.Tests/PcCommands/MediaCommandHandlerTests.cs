using Microsoft.Extensions.Logging.Abstractions;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.Tests.PcCommands;

public sealed class MediaCommandHandlerTests
{
    [Theory]
    [InlineData(PcCommandIds.MediaPlay, "play")]
    [InlineData(PcCommandIds.MediaPause, "pause")]
    [InlineData(PcCommandIds.MediaPlayPause, "playPause")]
    [InlineData(PcCommandIds.MediaPrevious, "previous")]
    [InlineData(PcCommandIds.MediaNext, "next")]
    public async Task DispatchAsync_AllowlistedMediaCommand_UsesMediaService(
        string commandId,
        string expectedOperation)
    {
        var mediaService = new RecordingMediaSessionService();
        var dispatcher = CreateDispatcher(new MediaCommandHandler(commandId, mediaService));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(commandId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedOperation, mediaService.LastOperation);
    }

    [Fact]
    public async Task DispatchAsync_Seek_PassesValidatedPositionToMediaService()
    {
        var mediaService = new RecordingMediaSessionService();
        var dispatcher = CreateDispatcher(
            new MediaCommandHandler(PcCommandIds.MediaSeek, mediaService));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(PcCommandIds.MediaSeek, 42_500),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("seek", mediaService.LastOperation);
        Assert.Equal(TimeSpan.FromMilliseconds(42_500), mediaService.LastSeekPosition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public async Task DispatchAsync_SeekWithInvalidPosition_IsRejectedBeforeMediaService(
        long? positionMilliseconds)
    {
        var mediaService = new RecordingMediaSessionService();
        var dispatcher = CreateDispatcher(
            new MediaCommandHandler(PcCommandIds.MediaSeek, mediaService));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(PcCommandIds.MediaSeek, positionMilliseconds),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The requested media position is invalid.", result.Message);
        Assert.Null(mediaService.LastOperation);
    }

    [Fact]
    public async Task DispatchAsync_WhenSessionDisappears_ReturnsMediaServiceFailure()
    {
        var mediaService = new RecordingMediaSessionService
        {
            NextResult = MediaSessionCommandResult.Failure(
                "The active media session is no longer available.")
        };
        var dispatcher = CreateDispatcher(
            new MediaCommandHandler(PcCommandIds.MediaNext, mediaService));

        var result = await dispatcher.DispatchAsync(
            new PcCommandInvocation(PcCommandIds.MediaNext),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The active media session is no longer available.", result.Message);
    }

    private static PcCommandDispatcher CreateDispatcher(IPcCommandHandler handler) =>
        new([handler], NullLogger<PcCommandDispatcher>.Instance);

    private sealed class RecordingMediaSessionService : IMediaSessionService
    {
        public MediaState Current { get; } = MediaState.Inactive(DateTimeOffset.UtcNow);

        public string? LastOperation { get; private set; }

        public TimeSpan? LastSeekPosition { get; private set; }

        public MediaSessionCommandResult NextResult { get; init; } =
            MediaSessionCommandResult.Success("Media command completed.");

        public Task<MediaSessionCommandResult> PlayAsync(CancellationToken cancellationToken) =>
            RecordAsync("play", cancellationToken);

        public Task<MediaSessionCommandResult> PauseAsync(CancellationToken cancellationToken) =>
            RecordAsync("pause", cancellationToken);

        public Task<MediaSessionCommandResult> TogglePlayPauseAsync(
            CancellationToken cancellationToken) =>
            RecordAsync("playPause", cancellationToken);

        public Task<MediaSessionCommandResult> SkipPreviousAsync(
            CancellationToken cancellationToken) =>
            RecordAsync("previous", cancellationToken);

        public Task<MediaSessionCommandResult> SkipNextAsync(
            CancellationToken cancellationToken) =>
            RecordAsync("next", cancellationToken);

        public Task<MediaSessionCommandResult> SeekAsync(
            TimeSpan position,
            CancellationToken cancellationToken)
        {
            LastSeekPosition = position;
            return RecordAsync("seek", cancellationToken);
        }

        private Task<MediaSessionCommandResult> RecordAsync(
            string operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOperation = operation;
            return Task.FromResult(NextResult);
        }
    }
}
