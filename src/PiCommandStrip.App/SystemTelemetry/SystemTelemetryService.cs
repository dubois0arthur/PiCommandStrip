using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.SystemTelemetry;

internal sealed class SystemTelemetryService(
    IHardwareTelemetryProvider provider,
    SystemTelemetryNormalizer normalizer,
    SystemTelemetryStateStore stateStore,
    ISystemTelemetryStateBroadcaster broadcaster,
    SystemTelemetryConfiguration configuration,
    ILogger<SystemTelemetryService> logger) : BackgroundService, ISystemTelemetryService
{
    private string? _lastDiagnosticsKey;
    private string? _lastCollectionFailureType;

    public SystemTelemetryState Current => stateStore.Current;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.Enabled)
        {
            logger.LogInformation("System telemetry collection is disabled by configuration");
            return;
        }

        logger.LogInformation(
            "System telemetry collection started with provider {ProviderName} at {IntervalMilliseconds} ms",
            WindowsHardwareTelemetryProvider.ProviderDisplayName,
            configuration.PollInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CollectOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(configuration.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = normalizer.Normalize(provider.Read());
            if (_lastCollectionFailureType is not null)
            {
                logger.LogInformation("System telemetry collection recovered");
                _lastCollectionFailureType = null;
            }
            LogDiagnosticsWhenChanged(state.Diagnostics);
            if (stateStore.TryUpdate(state, out var changedState))
            {
                await broadcaster.BroadcastAsync(changedState, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureType = exception.GetType().Name;
            if (!string.Equals(_lastCollectionFailureType, failureType, StringComparison.Ordinal))
            {
                logger.LogWarning(exception, "System telemetry sample failed; collection will retry");
                _lastCollectionFailureType = failureType;
            }
        }
    }

    private void LogDiagnosticsWhenChanged(SystemTelemetryDiagnosticsState diagnostics)
    {
        var key = string.Join(
            "|",
            diagnostics.ProviderStatus,
            diagnostics.CpuTemperatureSensor,
            diagnostics.GpuIdentifier,
            diagnostics.GpuTemperatureSensor,
            string.Join(";", diagnostics.UnavailableReasons));
        if (string.Equals(key, _lastDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDiagnosticsKey = key;
        logger.LogInformation(
            "System telemetry provider {ProviderStatus}; CPU sensor {CpuSensor}; GPU {GpuName} ({GpuIdentifier}); GPU sensor {GpuSensor}; unavailable: {UnavailableReasons}",
            diagnostics.ProviderStatus,
            diagnostics.CpuTemperatureSensor ?? "none",
            diagnostics.GpuName ?? "none",
            diagnostics.GpuIdentifier ?? "none",
            diagnostics.GpuTemperatureSensor ?? "none",
            diagnostics.UnavailableReasons.Count == 0
                ? "none"
                : string.Join("; ", diagnostics.UnavailableReasons));
    }
}
