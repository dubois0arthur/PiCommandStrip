namespace PiCommandStrip.App.ForegroundWindows;

public sealed class ForegroundStateMonitor(
    IForegroundWindowProvider foregroundWindowProvider,
    ForegroundStateStore stateStore,
    IPcStateBroadcaster broadcaster)
{
    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var observation = await foregroundWindowProvider.ObserveAsync(cancellationToken);

        if (stateStore.TryUpdate(observation, out var changedState))
        {
            await broadcaster.BroadcastAsync(changedState, cancellationToken);
        }
    }
}
