namespace PiCommandStrip.App.BrowserIntegration;

public interface IBrowserIntegrationService
{
    BrowserState Current { get; }

    Task BeginConnectionAsync(
        Guid connectionId,
        BrowserIdentity identity,
        CancellationToken cancellationToken);

    Task ApplyObservationAsync(
        Guid connectionId,
        BrowserTabObservation observation,
        CancellationToken cancellationToken);

    Task EndConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    Task SetBrowserContextActiveAsync(bool isActive, CancellationToken cancellationToken);
}

public interface IBrowserStateBroadcaster
{
    Task BroadcastAsync(BrowserState state, CancellationToken cancellationToken);
}
