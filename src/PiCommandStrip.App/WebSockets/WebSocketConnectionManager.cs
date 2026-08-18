using System.Collections.Concurrent;
using System.Net.WebSockets;
using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.Protocol;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketConnectionManager(
    ServerMessageFactory messageFactory,
    TimeProvider timeProvider,
    TimeSpan commandCooldown,
    ILogger<WebSocketConnectionManager> logger)
{
    private static readonly TimeSpan ClientSendTimeout = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<Guid, WebSocketClientConnection> _connections = new();

    public WebSocketClientConnection Add(WebSocket socket)
    {
        var connection = new WebSocketClientConnection(socket, timeProvider, commandCooldown);

        if (!_connections.TryAdd(connection.ConnectionId, connection))
        {
            throw new InvalidOperationException("Could not register the WebSocket connection.");
        }

        return connection;
    }

    public void Remove(Guid connectionId) => _connections.TryRemove(connectionId, out _);

    public async Task BroadcastPcStateAsync(
        ForegroundWindowState state,
        CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.IsReadyForBroadcasts)
            .Select(connection => SendSafelyAsync(connection, state, cancellationToken));

        await Task.WhenAll(sends);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task BroadcastContextStateAsync(
        ContextState state,
        CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.IsReadyForBroadcasts)
            .Select(connection => SendContextSafelyAsync(connection, state, cancellationToken));

        await Task.WhenAll(sends);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task BroadcastMediaStateAsync(
        MediaState state,
        CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.IsReadyForBroadcasts)
            .Select(connection => SendMediaSafelyAsync(connection, state, cancellationToken));

        await Task.WhenAll(sends);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task BroadcastAudioStateAsync(
        AudioState state,
        CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.IsReadyForBroadcasts)
            .Select(connection => SendAudioSafelyAsync(connection, state, cancellationToken));

        await Task.WhenAll(sends);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task BroadcastSpotifyStateAsync(
        SpotifyState state,
        CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.IsReadyForBroadcasts)
            .Select(connection => SendSpotifySafelyAsync(connection, state, cancellationToken));

        await Task.WhenAll(sends);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task SendSafelyAsync(
        WebSocketClientConnection connection,
        ForegroundWindowState state,
        CancellationToken cancellationToken)
    {
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCancellation.CancelAfter(ClientSendTimeout);

        try
        {
            await connection.SendPcStateAsync(state, messageFactory, sendCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown owns cancellation; the connection handler will close the socket.
        }
        catch (Exception exception)
        {
            Remove(connection.ConnectionId);
            connection.Abort();
            logger.LogWarning(
                exception,
                "Removed WebSocket connection {ConnectionId} after a broadcast failure",
                connection.ConnectionId);
        }
    }

    private async Task SendContextSafelyAsync(
        WebSocketClientConnection connection,
        ContextState state,
        CancellationToken cancellationToken)
    {
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCancellation.CancelAfter(ClientSendTimeout);

        try
        {
            await connection.SendContextStateAsync(state, messageFactory, sendCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown owns cancellation; the connection handler will close the socket.
        }
        catch (Exception exception)
        {
            Remove(connection.ConnectionId);
            connection.Abort();
            logger.LogWarning(
                exception,
                "Removed WebSocket connection {ConnectionId} after a context broadcast failure",
                connection.ConnectionId);
        }
    }

    private async Task SendMediaSafelyAsync(
        WebSocketClientConnection connection,
        MediaState state,
        CancellationToken cancellationToken)
    {
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCancellation.CancelAfter(ClientSendTimeout);

        try
        {
            await connection.SendMediaStateAsync(state, messageFactory, sendCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown owns cancellation; the connection handler will close the socket.
        }
        catch (Exception exception)
        {
            Remove(connection.ConnectionId);
            connection.Abort();
            logger.LogWarning(
                exception,
                "Removed WebSocket connection {ConnectionId} after a media broadcast failure",
                connection.ConnectionId);
        }
    }

    private async Task SendAudioSafelyAsync(
        WebSocketClientConnection connection,
        AudioState state,
        CancellationToken cancellationToken)
    {
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCancellation.CancelAfter(ClientSendTimeout);

        try
        {
            await connection.SendAudioStateAsync(state, messageFactory, sendCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown owns cancellation; the connection handler will close the socket.
        }
        catch (Exception exception)
        {
            Remove(connection.ConnectionId);
            connection.Abort();
            logger.LogWarning(
                exception,
                "Removed WebSocket connection {ConnectionId} after an audio broadcast failure",
                connection.ConnectionId);
        }
    }

    private async Task SendSpotifySafelyAsync(
        WebSocketClientConnection connection,
        SpotifyState state,
        CancellationToken cancellationToken)
    {
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCancellation.CancelAfter(ClientSendTimeout);

        try
        {
            await connection.SendSpotifyStateAsync(state, messageFactory, sendCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown owns cancellation; the connection handler will close the socket.
        }
        catch (Exception exception)
        {
            Remove(connection.ConnectionId);
            connection.Abort();
            logger.LogWarning(
                exception,
                "Removed WebSocket connection {ConnectionId} after a Spotify broadcast failure",
                connection.ConnectionId);
        }
    }
}
