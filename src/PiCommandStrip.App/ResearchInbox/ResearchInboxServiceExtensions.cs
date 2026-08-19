namespace PiCommandStrip.App.ResearchInbox;

public static class ResearchInboxServiceExtensions
{
    public static IServiceCollection AddResearchInbox(this IServiceCollection services)
    {
        services.AddSingleton<IResearchInboxService>(serviceProvider =>
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PiCommandStrip");
            return new SqliteResearchInboxService(
                Path.Combine(dataDirectory, "research-inbox.v1.db"),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IResearchInboxStateBroadcaster>(),
                serviceProvider.GetRequiredService<ILogger<SqliteResearchInboxService>>());
        });
        services.AddHostedService<ResearchInboxInitializationService>();
        return services;
    }
}

internal sealed class ResearchInboxInitializationService(
    IResearchInboxService service,
    ILogger<ResearchInboxInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await service.GetPageAsync(null, 1, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Research Inbox initialization failed with error type {ErrorType}",
                exception.GetType().Name);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

