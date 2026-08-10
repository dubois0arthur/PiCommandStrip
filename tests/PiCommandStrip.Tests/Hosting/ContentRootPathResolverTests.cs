using PiCommandStrip.App.Hosting;

namespace PiCommandStrip.Tests.Hosting;

public sealed class ContentRootPathResolverTests
{
    [Fact]
    public void Resolve_PrefersCurrentDirectoryWhenItContainsWebRoot()
    {
        var root = CreateTemporaryDirectory();
        var currentDirectory = Path.Combine(root, "project");
        var applicationBaseDirectory = Path.Combine(root, "published");
        Directory.CreateDirectory(Path.Combine(currentDirectory, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(applicationBaseDirectory, "wwwroot"));

        try
        {
            var result = ContentRootPathResolver.Resolve(currentDirectory, applicationBaseDirectory);

            Assert.Equal(currentDirectory, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesPublishedApplicationDirectoryWhenCurrentDirectoryHasNoWebRoot()
    {
        var root = CreateTemporaryDirectory();
        var currentDirectory = Path.Combine(root, "shortcut-working-directory");
        var applicationBaseDirectory = Path.Combine(root, "published");
        Directory.CreateDirectory(currentDirectory);
        Directory.CreateDirectory(Path.Combine(applicationBaseDirectory, "wwwroot"));

        try
        {
            var result = ContentRootPathResolver.Resolve(currentDirectory, applicationBaseDirectory);

            Assert.Equal(applicationBaseDirectory, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PiCommandStrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
