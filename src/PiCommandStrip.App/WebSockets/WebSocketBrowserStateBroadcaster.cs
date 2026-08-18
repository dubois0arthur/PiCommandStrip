using PiCommandStrip.App.BrowserIntegration;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketBrowserStateBroadcaster(WebSocketConnectionManager connectionManager)
    : IBrowserStateBroadcaster
{
    public Task BroadcastAsync(BrowserState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastBrowserStateAsync(state, cancellationToken);
}
