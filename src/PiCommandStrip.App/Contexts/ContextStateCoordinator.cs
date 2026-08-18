using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.BrowserIntegration;

namespace PiCommandStrip.App.Contexts;

public interface IContextStateBroadcaster
{
    Task BroadcastAsync(ContextState state, CancellationToken cancellationToken);
}

public sealed class ContextStateCoordinator(
    ContextStateStore stateStore,
    IContextStateBroadcaster broadcaster,
    IBrowserIntegrationService? browserIntegrationService = null)
{
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public ContextState Current => stateStore.Current;

    public Task ObserveForegroundAsync(
        ForegroundWindowState foreground,
        CancellationToken cancellationToken) =>
        UpdateAsync(() => ObserveForeground(foreground), cancellationToken);

    public Task<ContextSelectionUpdate> PinAsync(
        string contextId,
        CancellationToken cancellationToken) =>
        UpdateAsync(() => stateStore.Pin(contextId), cancellationToken);

    public Task<ContextSelectionUpdate> UseAutomaticAsync(CancellationToken cancellationToken) =>
        UpdateAsync(stateStore.UseAutomatic, cancellationToken);

    private ContextSelectionUpdate ObserveForeground(ForegroundWindowState foreground)
    {
        var changed = stateStore.TryUpdateForeground(foreground, out var state);
        return new ContextSelectionUpdate(true, changed, state, string.Empty);
    }

    private async Task<ContextSelectionUpdate> UpdateAsync(
        Func<ContextSelectionUpdate> update,
        CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken);

        try
        {
            var result = update();
            if (result.Changed)
            {
                if (browserIntegrationService is not null)
                {
                    await browserIntegrationService.SetBrowserContextActiveAsync(
                        result.State.ContextId == ContextIds.Browser,
                        cancellationToken);
                }
                await broadcaster.BroadcastAsync(result.State, cancellationToken);
            }

            return result;
        }
        finally
        {
            _updateLock.Release();
        }
    }
}
