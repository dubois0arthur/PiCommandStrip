using PiCommandStrip.App.SystemTelemetry;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketSystemTelemetryStateBroadcaster(
    WebSocketConnectionManager connectionManager) : ISystemTelemetryStateBroadcaster
{
    public Task BroadcastAsync(SystemTelemetryState state, CancellationToken cancellationToken) =>
        connectionManager.BroadcastSystemTelemetryAsync(state, cancellationToken);
}
