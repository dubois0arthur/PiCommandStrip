using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.Tests.Contexts;

public sealed class ContextStateStoreTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryUpdateForeground_AutomaticMode_ChangesResolvedContext()
    {
        var (store, clock) = CreateStore();
        clock.Advance(TimeSpan.FromSeconds(1));

        var changed = store.TryUpdateForeground(
            Available("spotify", "Now playing"),
            out var media);

        Assert.True(changed);
        Assert.Equal(ContextIds.Media, media.ContextId);
        Assert.Equal(ContextSelectionModes.Automatic, media.SelectionMode);
        Assert.Equal(ContextSources.ForegroundProcess, media.Source);
        Assert.Equal("spotify", media.ForegroundProcess);
        Assert.Equal("Now playing", media.ForegroundWindowTitle);
        Assert.Equal(InitialTime.AddSeconds(1), media.ActiveSinceUtc);

        clock.Advance(TimeSpan.FromSeconds(1));
        store.TryUpdateForeground(Available("chrome", "Research"), out var browser);

        Assert.Equal(ContextIds.Browser, browser.ContextId);
        Assert.Equal(InitialTime.AddSeconds(2), browser.ActiveSinceUtc);
    }

    [Fact]
    public void Pin_ForegroundChanges_ManualContextRemainsActive()
    {
        var (store, clock) = CreateStore();
        store.TryUpdateForeground(Available("spotify", "Now playing"), out _);
        clock.Advance(TimeSpan.FromSeconds(1));

        var pinned = store.Pin(ContextIds.Gaming);

        Assert.True(pinned.Succeeded);
        Assert.True(pinned.Changed);
        Assert.Equal(ContextIds.Gaming, pinned.State.ContextId);
        Assert.Equal(ContextSelectionModes.Manual, pinned.State.SelectionMode);
        Assert.Equal(ContextSources.ManualOverride, pinned.State.Source);

        var activeSinceUtc = pinned.State.ActiveSinceUtc;
        clock.Advance(TimeSpan.FromSeconds(1));
        store.TryUpdateForeground(Available("chrome", "Research"), out var stillPinned);

        Assert.Equal(ContextIds.Gaming, stillPinned.ContextId);
        Assert.Equal("chrome", stillPinned.ForegroundProcess);
        Assert.Equal(activeSinceUtc, stillPinned.ActiveSinceUtc);
    }

    [Fact]
    public void UseAutomatic_AfterManualPin_ImmediatelyResolvesLatestForeground()
    {
        var (store, _) = CreateStore();
        store.TryUpdateForeground(Available("spotify", "Now playing"), out _);
        store.Pin(ContextIds.Gaming);
        store.TryUpdateForeground(Available("chrome", "Research"), out _);

        var automatic = store.UseAutomatic();

        Assert.True(automatic.Succeeded);
        Assert.True(automatic.Changed);
        Assert.Equal(ContextIds.Browser, automatic.State.ContextId);
        Assert.Equal(ContextSelectionModes.Automatic, automatic.State.SelectionMode);
        Assert.Equal(ContextSources.ForegroundProcess, automatic.State.Source);
    }

    [Fact]
    public void Pin_UnknownContext_LeavesStateUnchanged()
    {
        var (store, _) = CreateStore();
        var before = store.Current;

        var result = store.Pin("unknown");

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(before, store.Current);
    }

    private static (ContextStateStore Store, AdjustableTimeProvider Clock) CreateStore()
    {
        var clock = new AdjustableTimeProvider(InitialTime);
        var catalog = new ContextCatalog();
        var resolver = new ForegroundProcessContextResolver(
            catalog,
            new ContextOptions
            {
                ProcessMappings = new Dictionary<string, string[]>
                {
                    [ContextIds.Media] = ["spotify"],
                    [ContextIds.Browser] = ["chrome"]
                }
            });

        return (new ContextStateStore(catalog, resolver, clock), clock);
    }

    private static ForegroundWindowState Available(string processName, string title) =>
        new(true, processName, 42, title, InitialTime);

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
