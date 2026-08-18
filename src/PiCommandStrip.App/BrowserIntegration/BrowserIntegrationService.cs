namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserIntegrationService(
    BrowserStateStore store,
    BrowserStateNormalizer normalizer,
    IBrowserStateBroadcaster broadcaster,
    TimeProvider timeProvider) : IBrowserIntegrationService
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private Guid? _activeConnectionId;
    private bool _browserContextActive;

    public BrowserState Current => store.Current;

    public async Task BeginConnectionAsync(
        Guid connectionId,
        BrowserIdentity identity,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _activeConnectionId = connectionId;
            await PublishIfChangedAsync(
                normalizer.Connected(identity, timeProvider.GetUtcNow()),
                cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task ApplyObservationAsync(
        Guid connectionId,
        BrowserTabObservation observation,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_activeConnectionId != connectionId || !store.Current.IsConnected)
            {
                return;
            }

            await PublishIfChangedAsync(
                RemoveSelectionWhenContextInactive(normalizer.Normalize(
                    store.Current,
                    observation,
                    timeProvider.GetUtcNow())),
                cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task SetBrowserContextActiveAsync(
        bool isActive,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _browserContextActive = isActive;
            var current = store.Current;
            if (!isActive && current.SelectedText is not null)
            {
                await PublishIfChangedAsync(
                    current with
                    {
                        SelectedText = null,
                        LastUpdatedUtc = timeProvider.GetUtcNow()
                    },
                    cancellationToken);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task EndConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_activeConnectionId != connectionId)
            {
                return;
            }

            _activeConnectionId = null;
            await PublishIfChangedAsync(
                BrowserState.Disconnected(timeProvider.GetUtcNow()),
                cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task PublishIfChangedAsync(
        BrowserState state,
        CancellationToken cancellationToken)
    {
        if (store.TryUpdate(state))
        {
            await broadcaster.BroadcastAsync(state, cancellationToken);
        }
    }

    private BrowserState RemoveSelectionWhenContextInactive(BrowserState state) =>
        _browserContextActive ? state : state with { SelectedText = null };
}
