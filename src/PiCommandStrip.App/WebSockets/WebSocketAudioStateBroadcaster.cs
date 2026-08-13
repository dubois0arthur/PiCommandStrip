using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketAudioStateBroadcaster(WebSocketConnectionManager connectionManager)
    : IAudioStateBroadcaster
{
    public Task BroadcastAsync(AudioState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastAudioStateAsync(state, cancellationToken);
}
