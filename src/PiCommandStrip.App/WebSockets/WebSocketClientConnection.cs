using System.Net.WebSockets;
using System.Text.Json;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketClientConnection(WebSocket socket, TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ForegroundWindowState? _lastSentPcState;
    private int _isReadyForBroadcasts;

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public WebSocket Socket { get; } = socket;

    public PcCommandCooldown CommandCooldown { get; } =
        new(timeProvider, PcCommandCooldown.DefaultDuration);

    public bool IsReadyForBroadcasts => Volatile.Read(ref _isReadyForBroadcasts) == 1;

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

    public async Task InitializePcStateAsync(
        ForegroundStateStore stateStore,
        ServerMessageFactory messageFactory,
        CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            Volatile.Write(ref _isReadyForBroadcasts, 1);
            await SendPcStateCoreAsync(stateStore.Current, messageFactory, cancellationToken);
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
