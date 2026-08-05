using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketPcStateBroadcaster(WebSocketConnectionManager connectionManager)
    : IPcStateBroadcaster
{
    public Task BroadcastAsync(ForegroundWindowState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastPcStateAsync(state, cancellationToken);
}
