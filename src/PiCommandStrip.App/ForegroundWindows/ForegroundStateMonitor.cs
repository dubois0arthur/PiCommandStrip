using PiCommandStrip.App.Contexts;

namespace PiCommandStrip.App.ForegroundWindows;

public sealed class ForegroundStateMonitor(
    IForegroundWindowProvider foregroundWindowProvider,
    ForegroundStateStore stateStore,
    IPcStateBroadcaster broadcaster,
    ContextStateCoordinator contextStateCoordinator)
{
    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var observation = await foregroundWindowProvider.ObserveAsync(cancellationToken);

        if (stateStore.TryUpdate(observation, out var changedState))
        {
            await broadcaster.BroadcastAsync(changedState, cancellationToken);
            await contextStateCoordinator.ObserveForegroundAsync(changedState, cancellationToken);
        }
    }
}
