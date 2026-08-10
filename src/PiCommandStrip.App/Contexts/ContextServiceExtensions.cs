using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.Contexts;

public static class ContextServiceExtensions
{
    public static IServiceCollection AddPiCommandStripContexts(
        this IServiceCollection services,
        ContextOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<ContextCatalog>();
        services.AddSingleton<IContextResolver, ForegroundProcessContextResolver>();
        services.AddSingleton<ContextStateStore>();
        services.AddSingleton<ContextStateCoordinator>();

        return services;
    }
}
