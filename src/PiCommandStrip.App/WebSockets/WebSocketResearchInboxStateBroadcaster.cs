using PiCommandStrip.App.ResearchInbox;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketResearchInboxStateBroadcaster(
    WebSocketConnectionManager connectionManager) : IResearchInboxStateBroadcaster
{
    public Task BroadcastAsync(ResearchInboxState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastResearchInboxStateAsync(state, cancellationToken);
}
