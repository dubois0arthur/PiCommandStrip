namespace PiCommandStrip.App.Hosting;

public static class ContentRootPathResolver
{
    public static string Resolve(string currentDirectory, string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        if (HasWebRoot(currentDirectory))
        {
            return currentDirectory;
        }

        if (HasWebRoot(applicationBaseDirectory))
        {
            return applicationBaseDirectory;
        }

        return currentDirectory;
    }

    private static bool HasWebRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, "wwwroot"));
}
