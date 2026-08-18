using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PiCommandStrip.App.Authentication;
using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;
using PiCommandStrip.App.Spotify;
using PiCommandStrip.App.WebSockets;

namespace PiCommandStrip.Tests.WebSockets;

public sealed class WebSocketAuthenticationTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ValidToken = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    [Fact]
    public async Task ClientHello_WithValidToken_AuthenticatesAndReceivesPcState()
    {
        var fixture = CreateFixture(ClientHello(ValidToken));

        await fixture.RunAsync();

        Assert.True(fixture.Connection.IsAuthenticated);
        Assert.Contains(MessageTypes.PcState, fixture.Socket.SentMessageTypes);
        Assert.Contains(MessageTypes.ContextState, fixture.Socket.SentMessageTypes);
        Assert.Contains(MessageTypes.MediaState, fixture.Socket.SentMessageTypes);
        Assert.Contains(MessageTypes.AudioState, fixture.Socket.SentMessageTypes);
        Assert.Contains(MessageTypes.SpotifyState, fixture.Socket.SentMessageTypes);
    }

    [Fact]
    public async Task ClientHello_WithoutToken_IsRejected()
    {
        var fixture = CreateFixture(ClientHello(null));

        await fixture.RunAsync();

        Assert.False(fixture.Connection.IsAuthenticated);
        Assert.Contains("authentication_missing", fixture.Socket.SentErrorCodes);
    }

    [Fact]
    public async Task ClientHello_WithIncorrectToken_IsRejected()
    {
        var incorrectToken = Convert.ToBase64String(new byte[32]);
        var fixture = CreateFixture(ClientHello(incorrectToken));

        await fixture.RunAsync();

        Assert.False(fixture.Connection.IsAuthenticated);
        Assert.Contains("authentication_failed", fixture.Socket.SentErrorCodes);
    }

    [Fact]
    public async Task ClientHello_WithExpiredTimestamp_IsRejected()
    {
        var fixture = CreateFixture(ClientHello(
            ValidToken,
            CurrentTime - ClientAuthenticationService.MaximumAttemptAge - TimeSpan.FromSeconds(1)));

        await fixture.RunAsync();

        Assert.False(fixture.Connection.IsAuthenticated);
        Assert.Contains("authentication_expired", fixture.Socket.SentErrorCodes);
    }

    [Fact]
    public async Task CommandRequest_BeforeAuthentication_IsRejectedWithoutDispatch()
    {
        var fixture = CreateFixture(CommandRequest());

        await fixture.RunAsync();

        Assert.Equal(0, fixture.CommandDispatcher.DispatchCount);
        Assert.Contains("authentication_required", fixture.Socket.SentErrorCodes);
    }

    [Fact]
    public async Task CommandRequest_AfterAuthentication_DispatchesAllowlistedIdentifier()
    {
        var fixture = CreateFixture(ClientHello(ValidToken), CommandRequest());

        await fixture.RunAsync();

        Assert.Equal(1, fixture.CommandDispatcher.DispatchCount);
        Assert.Equal(PcCommandIds.OpenNotepad, fixture.CommandDispatcher.LastCommandId);
        Assert.Contains(MessageTypes.CommandResult, fixture.Socket.SentMessageTypes);
    }

    [Fact]
    public async Task MediaSeek_AfterAuthentication_DispatchesTypedPosition()
    {
        var fixture = CreateFixture(ClientHello(ValidToken), MediaSeekRequest(42_500));

        await fixture.RunAsync();

        Assert.Equal(PcCommandIds.MediaSeek, fixture.CommandDispatcher.LastCommandId);
        Assert.Equal(42_500, fixture.CommandDispatcher.LastPositionMilliseconds);
        Assert.Contains(MessageTypes.CommandResult, fixture.Socket.SentMessageTypes);
    }

    [Fact]
    public async Task ContextSelectionRequest_BeforeAuthentication_IsRejected()
    {
        var fixture = CreateFixture(ContextSelection(ContextIds.Media));

        await fixture.RunAsync();

        Assert.Contains("authentication_required", fixture.Socket.SentErrorCodes);
        Assert.Equal(ContextSelectionModes.Automatic, fixture.ContextCoordinator.Current.SelectionMode);
    }

    [Fact]
    public async Task ContextSelectionRequest_AfterAuthentication_PinsContext()
    {
        var fixture = CreateFixture(
            ClientHello(ValidToken),
            ContextSelection(ContextIds.Media));

        await fixture.RunAsync();

        Assert.Equal(ContextIds.Media, fixture.ContextCoordinator.Current.ContextId);
        Assert.Equal(ContextSelectionModes.Manual, fixture.ContextCoordinator.Current.SelectionMode);
        Assert.Contains(MessageTypes.ContextSelectionResult, fixture.Socket.SentMessageTypes);
    }

    [Fact]
    public void AuthenticationAttemptLimiter_BlocksRepeatedAttemptsUntilWindowExpires()
    {
        var timeProvider = new AdjustableTimeProvider(CurrentTime);
        var limiter = new AuthenticationAttemptLimiter(timeProvider, 2, TimeSpan.FromSeconds(30));

        Assert.True(limiter.TryBeginAttempt(out _));
        Assert.True(limiter.TryBeginAttempt(out _));
        Assert.False(limiter.TryBeginAttempt(out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(30), retryAfter);

        timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.True(limiter.TryBeginAttempt(out _));
    }

    private static WebSocketFixture CreateFixture(params byte[][] clientMessages)
    {
        var timeProvider = new AdjustableTimeProvider(CurrentTime);
        var socket = new ScriptedWebSocket(clientMessages);
        var connection = new WebSocketClientConnection(socket, timeProvider);
        var commandDispatcher = new RecordingCommandDispatcher();
        var contextCatalog = new ContextCatalog();
        var contextResolver = new ForegroundProcessContextResolver(
            contextCatalog,
            new ContextOptions());
        var contextStateStore = new ContextStateStore(
            contextCatalog,
            contextResolver,
            timeProvider);
        var contextCoordinator = new ContextStateCoordinator(
            contextStateStore,
            new RecordingContextStateBroadcaster());
        var handler = new WebSocketConnectionHandler(
            new ClientMessageParser(),
            new ServerMessageFactory(timeProvider),
            new WebSocketMessageReader(),
            new ForegroundStateStore(timeProvider),
            contextCoordinator,
            contextCatalog,
            new StubMediaSessionService(timeProvider),
            new StubAudioMixerService(timeProvider),
            new StubSpotifyService(timeProvider),
            commandDispatcher,
            new ClientAuthenticationService(ValidToken, timeProvider),
            new AuthenticationAttemptLimiter(timeProvider),
            timeProvider,
            new TestHostApplicationLifetime(),
            NullLogger<WebSocketConnectionHandler>.Instance);

        return new WebSocketFixture(
            handler,
            connection,
            socket,
            commandDispatcher,
            contextCoordinator);
    }

    private static byte[] ClientHello(string? token, DateTimeOffset? timestamp = null) =>
        token is null
            ? CreateEnvelope(MessageTypes.ClientHello, timestamp ?? CurrentTime, new
            {
                clientName = "test-dashboard",
                protocolVersion = ProtocolConstants.Version
            })
            : CreateEnvelope(MessageTypes.ClientHello, timestamp ?? CurrentTime, new
            {
                clientName = "test-dashboard",
                protocolVersion = ProtocolConstants.Version,
                authenticationToken = token
            });

    private static byte[] CommandRequest() =>
        CreateEnvelope(MessageTypes.CommandRequest, CurrentTime, new
        {
            commandId = PcCommandIds.OpenNotepad
        });

    private static byte[] MediaSeekRequest(long positionMilliseconds) =>
        CreateEnvelope(MessageTypes.CommandRequest, CurrentTime, new
        {
            commandId = PcCommandIds.MediaSeek,
            positionMilliseconds
        });

    private static byte[] ContextSelection(string contextId) =>
        CreateEnvelope(MessageTypes.ContextSelectionRequest, CurrentTime, new
        {
            mode = ContextSelectionModes.Manual,
            contextId
        });

    private static byte[] CreateEnvelope(string type, DateTimeOffset timestamp, object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            messageId = Guid.NewGuid(),
            timestampUtc = timestamp,
            payload
        });

    private sealed record WebSocketFixture(
        WebSocketConnectionHandler Handler,
        WebSocketClientConnection Connection,
        ScriptedWebSocket Socket,
        RecordingCommandDispatcher CommandDispatcher,
        ContextStateCoordinator ContextCoordinator)
    {
        public Task RunAsync() => Handler.HandleAsync(Connection, CancellationToken.None);
    }

    private sealed class RecordingContextStateBroadcaster : IContextStateBroadcaster
    {
        public Task BroadcastAsync(ContextState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StubMediaSessionService(TimeProvider timeProvider) : IMediaSessionService
    {
        public MediaState Current { get; } = MediaState.Inactive(timeProvider.GetUtcNow());

        public Task<MediaSessionCommandResult> PlayAsync(CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<MediaSessionCommandResult> PauseAsync(CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<MediaSessionCommandResult> TogglePlayPauseAsync(
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<MediaSessionCommandResult> SkipPreviousAsync(
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<MediaSessionCommandResult> SkipNextAsync(
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<MediaSessionCommandResult> SeekAsync(
            TimeSpan position,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        private static Task<MediaSessionCommandResult> NotAvailable(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MediaSessionCommandResult.Failure("No active media session."));
        }
    }

    private sealed class StubAudioMixerService(TimeProvider timeProvider) : IAudioMixerService
    {
        public AudioState Current { get; } = AudioState.Unavailable(timeProvider.GetUtcNow());

        public Task<AudioMixerCommandResult> SetMasterVolumeAsync(
            float volume,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<AudioMixerCommandResult> SetMasterMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<AudioMixerCommandResult> SetApplicationVolumeAsync(
            string applicationId,
            float volume,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<AudioMixerCommandResult> SetApplicationMuteAsync(
            string applicationId,
            bool isMuted,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<AudioMixerCommandResult> SetOutputDeviceAsync(
            string deviceId,
            CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        private static Task<AudioMixerCommandResult> NotAvailable(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AudioMixerCommandResult.Failure(
                "Windows audio is currently unavailable."));
        }
    }

    private sealed class RecordingCommandDispatcher : IPcCommandDispatcher
    {
        public int DispatchCount { get; private set; }

        public string? LastCommandId { get; private set; }

        public long? LastPositionMilliseconds { get; private set; }

        public Task<PcCommandExecutionResult> DispatchAsync(
            PcCommandInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DispatchCount++;
            LastCommandId = invocation.CommandId;
            LastPositionMilliseconds = invocation.PositionMilliseconds;
            return Task.FromResult(PcCommandExecutionResult.Success("Command completed."));
        }
    }

    private sealed class StubSpotifyService(TimeProvider timeProvider) : ISpotifyService
    {
        public SpotifyState Current { get; } = SpotifyState.Unconfigured(timeProvider.GetUtcNow());

        public Task<SpotifyCommandResult> SetSavedAsync(bool isSaved, CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<SpotifyCommandResult> SetShuffleAsync(bool enabled, CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        public Task<SpotifyCommandResult> SetRepeatAsync(string repeatState, CancellationToken cancellationToken) =>
            NotAvailable(cancellationToken);

        private static Task<SpotifyCommandResult> NotAvailable(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SpotifyCommandResult.Failure("Spotify is not configured."));
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class ScriptedWebSocket(IEnumerable<byte[]> receivedMessages) : WebSocket
    {
        private readonly Queue<byte[]> _receivedMessages = new(receivedMessages);
        private readonly List<byte[]> _sentMessages = [];
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public IReadOnlyList<string> SentMessageTypes => ReadSentStrings("type");

        public IReadOnlyList<string> SentErrorCodes => ReadSentStrings("code", "error");

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_receivedMessages.TryDequeue(out var message))
            {
                message.AsSpan().CopyTo(buffer.AsSpan());
                return Task.FromResult(new WebSocketReceiveResult(
                    message.Length,
                    WebSocketMessageType.Text,
                    true));
            }

            _state = WebSocketState.CloseReceived;
            return Task.FromResult(new WebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                true,
                WebSocketCloseStatus.NormalClosure,
                "Test complete."));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sentMessages.Add(buffer.ToArray());
            return Task.CompletedTask;
        }

        private IReadOnlyList<string> ReadSentStrings(string propertyName, string? requiredType = null)
        {
            var values = new List<string>();
            foreach (var bytes in _sentMessages)
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (requiredType is not null && root.GetProperty("type").GetString() != requiredType)
                {
                    continue;
                }

                var source = requiredType is null ? root : root.GetProperty("payload");
                var value = source.GetProperty(propertyName).GetString();
                if (value is not null)
                {
                    values.Add(value);
                }
            }

            return values;
        }
    }
}
