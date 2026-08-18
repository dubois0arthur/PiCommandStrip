using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketSpotifyStateBroadcaster(WebSocketConnectionManager connectionManager)
    : ISpotifyStateBroadcaster
{
    public Task BroadcastAsync(SpotifyState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastSpotifyStateAsync(state, cancellationToken);
}
