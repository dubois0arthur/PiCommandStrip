namespace PiCommandStrip.App.BrowserIntegration;

public interface IBrowserIntegrationService
{
    BrowserState Current { get; }

    Task BeginConnectionAsync(
        Guid connectionId,
        BrowserIdentity identity,
        IBrowserExtensionCommandChannel commandChannel,
        CancellationToken cancellationToken);

    Task ApplyObservationAsync(
        Guid connectionId,
        BrowserTabObservation observation,
        CancellationToken cancellationToken);

    Task EndConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    Task SetBrowserContextActiveAsync(bool isActive, CancellationToken cancellationToken);

    Task<BrowserExtensionCommandResult> ExecuteExtensionCommandAsync(
        BrowserExtensionCommand command,
        CancellationToken cancellationToken);
}

public interface IBrowserCommandService
{
    Task<BrowserCommandResult> ExecuteAsync(
        string commandId,
        string? searchActionId,
        CancellationToken cancellationToken);
}

public sealed record BrowserCommandResult(bool Succeeded, string Message);

public interface IBrowserStateBroadcaster
{
    Task BroadcastAsync(BrowserState state, CancellationToken cancellationToken);
}
