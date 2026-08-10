namespace PiCommandStrip.App.Contexts;

public sealed record ContextProfile(string Id, string DisplayName);

public sealed class ContextCatalog
{
    private readonly IReadOnlyDictionary<string, ContextProfile> _profilesById;

    public ContextCatalog()
    {
        Profiles =
        [
            new(ContextIds.Default, "Default"),
            new(ContextIds.Media, "Media"),
            new(ContextIds.Browser, "Browser / Research"),
            new(ContextIds.Gaming, "Gaming"),
            new(ContextIds.Audio, "Audio")
        ];
        _profilesById = Profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ContextProfile> Profiles { get; }

    public ContextProfile Default => _profilesById[ContextIds.Default];

    public bool TryGet(string contextId, out ContextProfile profile) =>
        _profilesById.TryGetValue(contextId, out profile!);
}
