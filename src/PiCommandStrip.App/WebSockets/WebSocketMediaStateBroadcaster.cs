using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketMediaStateBroadcaster(WebSocketConnectionManager connectionManager)
    : IMediaStateBroadcaster
{
    public Task BroadcastAsync(MediaState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastMediaStateAsync(state, cancellationToken);
}
