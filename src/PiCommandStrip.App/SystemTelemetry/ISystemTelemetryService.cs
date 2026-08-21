namespace PiCommandStrip.App.SystemTelemetry;

public interface ISystemTelemetryService
{
    SystemTelemetryState Current { get; }
}

public interface ISystemTelemetryStateBroadcaster
{
    Task BroadcastAsync(SystemTelemetryState state, CancellationToken cancellationToken);
}
