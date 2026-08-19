using System.Text.RegularExpressions;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed record BrowserSearchAction(string Id, string DisplayName, string UrlTemplate);

public sealed class BrowserSearchCatalog
{
    private const int MaximumActionIdLength = 50;
    private const int MaximumDisplayNameLength = 40;
    private const int MaximumTemplateLength = 1_000;
    private const int MaximumSearchUrlLength = 2_048;
    private static readonly Regex ActionIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,49}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, BrowserSearchAction> _actions;

    public BrowserSearchCatalog(BrowserIntegrationOptions options)
    {
        var actions = new Dictionary<string, BrowserSearchAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawId, configured) in options.SearchActions)
        {
            var id = rawId.Trim().ToLowerInvariant();
            var displayName = configured.DisplayName.Trim();
            var template = configured.UrlTemplate.Trim();
            if (!IsValidActionId(id) ||
                displayName.Length is 0 or > MaximumDisplayNameLength ||
                template.Length is 0 or > MaximumTemplateLength ||
                template.Split("{query}", StringSplitOptions.None).Length != 2 ||
                !TryValidateTemplate(template))
            {
                throw new InvalidOperationException(
                    $"Browser search action '{rawId}' must have a safe identifier, a bounded name, and one absolute HTTPS URL template containing '{{query}}'.");
            }

            if (!actions.TryAdd(id, new BrowserSearchAction(id, displayName, template)))
            {
                throw new InvalidOperationException($"Browser search action '{rawId}' is duplicated.");
            }
        }

        _actions = actions;
        Actions = actions.Values.OrderBy(action => action.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<BrowserSearchAction> Actions { get; }

    public bool TryBuildUrl(string? actionId, string? selectedText, out Uri? url)
    {
        url = null;
        if (!IsValidActionId(actionId) ||
            !_actions.TryGetValue(actionId!, out var action) ||
            string.IsNullOrWhiteSpace(selectedText))
        {
            return false;
        }

        var encodedQuery = Uri.EscapeDataString(selectedText.Trim());
        var candidate = action.UrlTemplate.Replace(
            "{query}",
            encodedQuery,
            StringComparison.Ordinal);
        if (candidate.Length > MaximumSearchUrlLength ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
            !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        url = parsed;
        return true;
    }

    public static bool IsValidActionId(string? actionId) =>
        actionId is { Length: > 0 and <= MaximumActionIdLength } &&
        ActionIdPattern.IsMatch(actionId);

    private static bool TryValidateTemplate(string template)
    {
        var sample = template.Replace("{query}", "research", StringComparison.Ordinal);
        return Uri.TryCreate(sample, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo);
    }
}
