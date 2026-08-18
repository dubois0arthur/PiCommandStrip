using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.Tests.PcCommands;

public sealed class SpotifyCommandHandlerTests
{
    [Fact]
    public async Task SetSaved_ValidTypedValue_DelegatesToSpotifyService()
    {
        var service = new RecordingSpotifyService();
        var handler = new SpotifyCommandHandler(PcCommandIds.SpotifySetSaved, service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(PcCommandIds.SpotifySetSaved, IsSaved: true),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(service.IsSaved);
    }

    [Fact]
    public async Task SetRepeat_InvalidState_IsRejectedWithoutCallingService()
    {
        var service = new RecordingSpotifyService();
        var handler = new SpotifyCommandHandler(PcCommandIds.SpotifySetRepeat, service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(PcCommandIds.SpotifySetRepeat, RepeatState: "forever"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task UnavailableSpotify_ReturnsNormalSafeCommandFailure()
    {
        var service = new RecordingSpotifyService
        {
            Result = SpotifyCommandResult.Failure("Spotify enrichment is temporarily unavailable.")
        };
        var handler = new SpotifyCommandHandler(PcCommandIds.SpotifySetShuffle, service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(PcCommandIds.SpotifySetShuffle, ShuffleEnabled: true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("token", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingSpotifyService : ISpotifyService
    {
        public SpotifyState Current { get; } = SpotifyState.Unconfigured(DateTimeOffset.UtcNow);
        public int CallCount { get; private set; }
        public bool? IsSaved { get; private set; }
        public SpotifyCommandResult Result { get; init; } = SpotifyCommandResult.Success("Changed.");

        public Task<SpotifyCommandResult> SetSavedAsync(bool isSaved, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IsSaved = isSaved;
            return Task.FromResult(Result);
        }

        public Task<SpotifyCommandResult> SetShuffleAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result);
        }

        public Task<SpotifyCommandResult> SetRepeatAsync(string repeatState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
