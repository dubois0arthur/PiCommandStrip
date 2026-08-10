using PiCommandStrip.App.Contexts;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketContextStateBroadcaster(WebSocketConnectionManager connectionManager)
    : IContextStateBroadcaster
{
    public Task BroadcastAsync(ContextState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastContextStateAsync(state, cancellationToken);
}
