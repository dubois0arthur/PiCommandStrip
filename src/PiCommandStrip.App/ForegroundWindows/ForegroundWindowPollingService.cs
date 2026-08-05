namespace PiCommandStrip.App.ForegroundWindows;

public sealed class ForegroundWindowPollingService(
    ForegroundStateMonitor monitor,
    ILogger<ForegroundWindowPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitor.CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected error while observing the foreground window");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
