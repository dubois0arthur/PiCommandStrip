using PiCommandStrip.App.ForegroundWindows;

namespace PiCommandStrip.App.Contexts;

public sealed class ContextStateStore
{
    private readonly Lock _sync = new();
    private readonly ContextCatalog _catalog;
    private readonly IContextResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private ForegroundWindowState _foreground;
    private ContextProfile? _manualProfile;
    private ContextState _current;

    public ContextStateStore(
        ContextCatalog catalog,
        IContextResolver resolver,
        TimeProvider timeProvider)
    {
        _catalog = catalog;
        _resolver = resolver;
        _timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();
        _foreground = ForegroundWindowState.Unavailable(now);
        var resolution = resolver.Resolve(new ContextSignals(_foreground));
        _current = CreateState(
            resolution.Profile,
            ContextSelectionModes.Automatic,
            resolution.Source,
            resolution.Trigger,
            now);
    }

    public ContextState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdateForeground(
        ForegroundWindowState foreground,
        out ContextState changedState)
    {
        lock (_sync)
        {
            _foreground = foreground;
            var next = _manualProfile is null
                ? CreateAutomaticState()
                : CreateState(
                    _manualProfile,
                    ContextSelectionModes.Manual,
                    ContextSources.ManualOverride,
                    _manualProfile.Id,
                    ActiveSinceFor(_manualProfile));

            return TryApply(next, out changedState);
        }
    }

    public ContextSelectionUpdate Pin(string contextId)
    {
        lock (_sync)
        {
            if (!_catalog.TryGet(contextId, out var profile))
            {
                return new ContextSelectionUpdate(
                    false,
                    false,
                    _current,
                    "Context identifier is not available.");
            }

            _manualProfile = profile;
            var next = CreateState(
                profile,
                ContextSelectionModes.Manual,
                ContextSources.ManualOverride,
                profile.Id,
                ActiveSinceFor(profile));
            var changed = TryApply(next, out var state);
            return new ContextSelectionUpdate(
                true,
                changed,
                state,
                $"{profile.DisplayName} context pinned.");
        }
    }

    public ContextSelectionUpdate UseAutomatic()
    {
        lock (_sync)
        {
            _manualProfile = null;
            var changed = TryApply(CreateAutomaticState(), out var state);
            return new ContextSelectionUpdate(
                true,
                changed,
                state,
                "Automatic context selection enabled.");
        }
    }

    private ContextState CreateAutomaticState()
    {
        var resolution = _resolver.Resolve(new ContextSignals(_foreground));
        return CreateState(
            resolution.Profile,
            ContextSelectionModes.Automatic,
            resolution.Source,
            resolution.Trigger,
            ActiveSinceFor(resolution.Profile));
    }

    private DateTimeOffset ActiveSinceFor(ContextProfile profile) =>
        string.Equals(_current.ContextId, profile.Id, StringComparison.Ordinal)
            ? _current.ActiveSinceUtc
            : _timeProvider.GetUtcNow();

    private ContextState CreateState(
        ContextProfile profile,
        string selectionMode,
        string source,
        string trigger,
        DateTimeOffset activeSinceUtc) =>
        new(
            profile.Id,
            profile.DisplayName,
            selectionMode,
            source,
            trigger,
            _foreground.IsAvailable ? _foreground.ProcessName : null,
            _foreground.IsAvailable ? _foreground.WindowTitle : null,
            activeSinceUtc);

    private bool TryApply(ContextState next, out ContextState state)
    {
        if (_current.HasSameMeaningAs(next))
        {
            state = _current;
            return false;
        }

        _current = next;
        state = next;
        return true;
    }
}
