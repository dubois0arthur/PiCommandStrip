namespace PiCommandStrip.App.PcCommands;

public static class PcCommandServiceExtensions
{
    public static IServiceCollection AddPcCommands(this IServiceCollection services)
    {
        services.AddSingleton<INotepadLauncher, WindowsNotepadLauncher>();
        services.AddSingleton<IPcCommandHandler, OpenNotepadCommandHandler>();
        services.AddSingleton<IPcCommandDispatcher, PcCommandDispatcher>();

        return services;
    }
}
