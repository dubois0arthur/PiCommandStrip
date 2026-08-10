using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.Tests.Contexts;

public sealed class ForegroundProcessContextResolverTests
{
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly ForegroundProcessContextResolver _resolver = CreateResolver();

    [Theory]
    [InlineData("Spotify", ContextIds.Media)]
    [InlineData("spotify.exe", ContextIds.Media)]
    [InlineData("firefox", ContextIds.Browser)]
    [InlineData("chrome", ContextIds.Browser)]
    [InlineData("msedge", ContextIds.Browser)]
    [InlineData("ExampleGame", ContextIds.Gaming)]
    public void Resolve_ConfiguredForegroundProcess_ReturnsMappedContext(
        string processName,
        string expectedContextId)
    {
        var result = _resolver.Resolve(new ContextSignals(Available(processName)));

        Assert.Equal(expectedContextId, result.Profile.Id);
        Assert.Equal(ContextSources.ForegroundProcess, result.Source);
    }

    [Fact]
    public void Resolve_UnmappedForegroundProcess_ReturnsDefaultFallback()
    {
        var result = _resolver.Resolve(new ContextSignals(Available("notepad")));

        Assert.Equal(ContextIds.Default, result.Profile.Id);
        Assert.Equal(ContextSources.Fallback, result.Source);
        Assert.Equal("notepad", result.Trigger);
    }

    [Fact]
    public void Resolve_UnavailableForeground_ReturnsDefaultFallback()
    {
        var result = _resolver.Resolve(new ContextSignals(
            ForegroundWindowState.Unavailable(ObservedAtUtc)));

        Assert.Equal(ContextIds.Default, result.Profile.Id);
        Assert.Equal("foreground_unavailable", result.Trigger);
    }

    [Fact]
    public void Constructor_ProcessMappedTwice_RejectsAmbiguousConfiguration()
    {
        var options = new ContextOptions
        {
            ProcessMappings = new Dictionary<string, string[]>
            {
                [ContextIds.Media] = ["shared"],
                [ContextIds.Gaming] = ["SHARED.exe"]
            }
        };

        Assert.Throws<InvalidOperationException>(() =>
            new ForegroundProcessContextResolver(new ContextCatalog(), options));
    }

    private static ForegroundProcessContextResolver CreateResolver() =>
        new(
            new ContextCatalog(),
            new ContextOptions
            {
                ProcessMappings = new Dictionary<string, string[]>
                {
                    [ContextIds.Media] = ["spotify"],
                    [ContextIds.Browser] = ["firefox", "chrome", "msedge"],
                    [ContextIds.Gaming] = ["examplegame"]
                }
            });

    private static ForegroundWindowState Available(string processName) =>
        new(true, processName, 42, $"{processName} window", ObservedAtUtc);
}
