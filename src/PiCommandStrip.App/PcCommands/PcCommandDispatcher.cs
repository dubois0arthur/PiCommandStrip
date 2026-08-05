namespace PiCommandStrip.App.PcCommands;

public sealed class PcCommandDispatcher : IPcCommandDispatcher
{
    private const string UnknownCommandMessage = "Command identifier is not allowlisted.";
    private const string SafeFailureMessage = "The command could not be completed.";

    private readonly IReadOnlyDictionary<string, IPcCommandHandler> _handlers;
    private readonly ILogger<PcCommandDispatcher> _logger;

    public PcCommandDispatcher(
        IEnumerable<IPcCommandHandler> handlers,
        ILogger<PcCommandDispatcher> logger)
    {
        _handlers = handlers.ToDictionary(handler => handler.CommandId, StringComparer.Ordinal);
        _logger = logger;
    }

    public async Task<PcCommandExecutionResult> DispatchAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(commandId, out var handler))
        {
            _logger.LogWarning("Rejected a PC command identifier that is not allowlisted");
            return PcCommandExecutionResult.Failure(UnknownCommandMessage);
        }

        _logger.LogInformation("Attempting allowlisted PC command {CommandId}", handler.CommandId);

        try
        {
            var result = await handler.ExecuteAsync(cancellationToken);
            _logger.LogInformation(
                "Allowlisted PC command {CommandId} completed with success {Succeeded}",
                handler.CommandId,
                result.Succeeded);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Allowlisted PC command {CommandId} was canceled", handler.CommandId);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Allowlisted PC command {CommandId} failed with error type {ErrorType}",
                handler.CommandId,
                exception.GetType().Name);
            return PcCommandExecutionResult.Failure(SafeFailureMessage);
        }
    }
}
