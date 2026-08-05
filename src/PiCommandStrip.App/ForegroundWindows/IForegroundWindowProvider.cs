namespace PiCommandStrip.App.ForegroundWindows;

public interface IForegroundWindowProvider
{
    ValueTask<ForegroundWindowState> ObserveAsync(CancellationToken cancellationToken);
}
