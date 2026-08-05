namespace PiCommandStrip.App.ForegroundWindows;

public interface IPcStateBroadcaster
{
    Task BroadcastAsync(ForegroundWindowState state, CancellationToken cancellationToken);
}
