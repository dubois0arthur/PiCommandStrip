using System.Collections.Concurrent;
using System.Net.WebSockets;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketConnectionManager(
    ServerMessageFactory messageFactory,
    TimeProvider timeProvider,
    ILogger<WebSocketConnectionManager> logger)
{
    private static readonly TimeSpan ClientSendTimeout = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<Guid, WebSocketClientConnection> _connections = new();

    public WebSocketClientConnection Add(WebSocket socket)
    {
        var connection = new WebSocketClientConnection(socket, timeProvider);

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
}
