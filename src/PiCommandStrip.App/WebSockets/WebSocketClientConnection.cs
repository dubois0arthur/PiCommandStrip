using System.Net.WebSockets;
using System.Text.Json;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketClientConnection(
    WebSocket socket,
    TimeProvider timeProvider,
    TimeSpan? commandCooldown = null)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ForegroundWindowState? _lastSentPcState;
    private ContextState? _lastSentContextState;
    private MediaState? _lastSentMediaState;
    private int _isAuthenticated;
    private int _isReadyForBroadcasts;

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public WebSocket Socket { get; } = socket;

    public PcCommandCooldown CommandCooldown { get; } =
        new(timeProvider, commandCooldown ?? PcCommandCooldown.DefaultDuration);

    public bool IsReadyForBroadcasts => Volatile.Read(ref _isReadyForBroadcasts) == 1;

    public bool IsAuthenticated => Volatile.Read(ref _isAuthenticated) == 1;

    public bool MarkAuthenticated() =>
        Interlocked.CompareExchange(ref _isAuthenticated, 1, 0) == 0;

    public async Task SendAsync<TPayload>(
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await SendCoreAsync(message, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task InitializeStateAsync(
        ForegroundStateStore stateStore,
        ContextStateCoordinator contextStateCoordinator,
        IMediaSessionService mediaSessionService,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            Volatile.Write(ref _isReadyForBroadcasts, 1);
            await SendPcStateCoreAsync(stateStore.Current, messageFactory, cancellationToken);
            await SendContextStateCoreAsync(
                contextStateCoordinator.Current,
                messageFactory,
                cancellationToken);
            await SendMediaStateCoreAsync(
                mediaSessionService.Current,
                messageFactory,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendPcStateAsync(
        ForegroundWindowState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (!IsReadyForBroadcasts)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await SendPcStateCoreAsync(state, messageFactory, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendContextStateAsync(
        ContextState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (!IsReadyForBroadcasts)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await SendContextStateCoreAsync(state, messageFactory, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendMediaStateAsync(
        MediaState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (!IsReadyForBroadcasts)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await SendMediaStateCoreAsync(state, messageFactory, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Abort() => Socket.Abort();

    private async Task SendPcStateCoreAsync(
        ForegroundWindowState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (_lastSentPcState is not null && _lastSentPcState.HasSameMeaningAs(state))
        {
            return;
        }

        var payload = new PcStatePayload(
            state.IsAvailable,
            state.ProcessName,
            state.ProcessId,
            state.WindowTitle,
            state.ObservedAtUtc);

        await SendCoreAsync(
            messageFactory.Create(MessageTypes.PcState, payload),
            cancellationToken);
        _lastSentPcState = state;
    }

    private async Task SendContextStateCoreAsync(
        ContextState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (_lastSentContextState is not null && _lastSentContextState.HasSameMeaningAs(state))
        {
            return;
        }

        var payload = new ContextStatePayload(
            state.ContextId,
            state.DisplayName,
            state.SelectionMode,
            state.Source,
            state.Trigger,
            state.ForegroundProcess,
            state.ForegroundWindowTitle,
            state.ActiveSinceUtc);

        await SendCoreAsync(
            messageFactory.Create(MessageTypes.ContextState, payload),
            cancellationToken);
        _lastSentContextState = state;
    }

    private async Task SendMediaStateCoreAsync(
        MediaState state,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        if (_lastSentMediaState is not null && _lastSentMediaState.HasSameMeaningAs(state))
        {
            return;
        }

        var payload = new MediaStatePayload(
            state.HasActiveSession,
            state.SessionSourceIdentifier,
            state.SourceName,
            state.Title,
            state.Artist,
            state.AlbumTitle,
            state.PlaybackState,
            ToMilliseconds(state.Position),
            ToMilliseconds(state.TotalDuration),
            state.SupportsPrevious,
            state.SupportsNext,
            state.SupportsPlay,
            state.SupportsPause,
            state.SupportsPlayPause,
            state.SupportsSeeking,
            state.LastUpdatedUtc,
            state.ArtworkId is null
                ? null
                : MediaArtworkCache.CreateUrl(state.ArtworkId));

        await SendCoreAsync(
            messageFactory.Create(MessageTypes.MediaState, payload),
            cancellationToken);
        _lastSentMediaState = state;
    }

    private static long? ToMilliseconds(TimeSpan? value) =>
        value is null ? null : (long)value.Value.TotalMilliseconds;

    private async Task SendCoreAsync<TPayload>(
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken)
    {
        if (Socket.State is not WebSocketState.Open)
        {
            throw new WebSocketException(WebSocketError.InvalidState);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.SerializerOptions);
        await Socket.SendAsync(json, WebSocketMessageType.Text, true, cancellationToken);
    }
}
