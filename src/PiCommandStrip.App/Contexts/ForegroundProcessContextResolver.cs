using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.Contexts;

public sealed class ForegroundProcessContextResolver : IContextResolver
{
    private readonly ContextCatalog _catalog;
    private readonly IReadOnlyDictionary<string, ContextProfile> _profilesByProcess;

    public ForegroundProcessContextResolver(ContextCatalog catalog, ContextOptions options)
    {
        _catalog = catalog;
        _profilesByProcess = BuildProcessMappings(catalog, options);
    }

    public ContextResolution Resolve(ContextSignals signals)
    {
        var foreground = signals.ForegroundWindow;
        if (!foreground.IsAvailable || string.IsNullOrWhiteSpace(foreground.ProcessName))
        {
            return new ContextResolution(
                _catalog.Default,
                ContextSources.Fallback,
                "foreground_unavailable");
        }

        var processName = NormalizeProcessName(foreground.ProcessName);
        if (_profilesByProcess.TryGetValue(processName, out var profile))
        {
            return new ContextResolution(
                profile,
                ContextSources.ForegroundProcess,
                processName);
        }

        return new ContextResolution(
            _catalog.Default,
            ContextSources.Fallback,
            processName);
    }

    private static IReadOnlyDictionary<string, ContextProfile> BuildProcessMappings(
        ContextCatalog catalog,
        ContextOptions options)
    {
        var profilesByProcess = new Dictionary<string, ContextProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in options.ProcessMappings)
        {
            if (!catalog.TryGet(mapping.Key, out var profile) || profile.Id == ContextIds.Default)
            {
                throw new InvalidOperationException(
                    $"Context process mappings contain unsupported context ID '{mapping.Key}'.");
            }

            foreach (var configuredProcessName in mapping.Value ?? [])
            {
                if (string.IsNullOrWhiteSpace(configuredProcessName))
                {
                    throw new InvalidOperationException(
                        $"Context process mappings for '{mapping.Key}' contain an empty process name.");
                }

                var processName = NormalizeProcessName(configuredProcessName);
                if (!profilesByProcess.TryAdd(processName, profile))
                {
                    throw new InvalidOperationException(
                        $"Process '{processName}' is mapped to more than one context.");
                }
            }
        }

        return profilesByProcess;
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }
}
