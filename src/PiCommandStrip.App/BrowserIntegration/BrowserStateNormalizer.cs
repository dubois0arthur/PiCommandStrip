namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserStateNormalizer
{
    public const int MaximumTitleLength = 512;
    public const int MaximumSelectedTextLength = 1000;
    public const int MaximumUrlLength = 2048;

    public BrowserState Connected(
        BrowserIdentity identity,
        DateTimeOffset timestampUtc) =>
        new(
            BrowserConnectionStates.Connected,
            NormalizeIdentifier(identity.BrowserType, 32)?.ToLowerInvariant(),
            NormalizeIdentifier(identity.SourceIdentifier, 128),
            NormalizeIdentifier(identity.InstanceIdentifier, 128),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            timestampUtc);

    public BrowserState Normalize(
        BrowserState connectedState,
        BrowserTabObservation observation,
        DateTimeOffset timestampUtc)
    {
        var (url, hostName) = NormalizeUrl(observation.Url);
        return connectedState with
        {
            ActiveTabId = observation.ActiveTabId is >= 0 ? observation.ActiveTabId : null,
            Url = url,
            HostName = hostName,
            PageTitle = NormalizeText(observation.PageTitle, MaximumTitleLength),
            SelectedText = NormalizeText(observation.SelectedText, MaximumSelectedTextLength),
            CanGoBack = observation.CanGoBack,
            CanGoForward = observation.CanGoForward,
            LastUpdatedUtc = timestampUtc
        };
    }

    public static (string? Url, string? HostName) NormalizeUrl(string? value)
    {
        var candidate = NormalizeText(value, MaximumUrlLength);
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return (null, null);
        }

        var normalized = uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
        return (normalized, uri.IdnHost.ToLowerInvariant());
    }

    private static string? NormalizeIdentifier(string? value, int maximumLength) =>
        NormalizeText(value, maximumLength);

    private static string? NormalizeText(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var length = maximumLength;
        if (char.IsHighSurrogate(normalized[length - 1]))
        {
            length--;
        }

        return normalized[..length];
    }
}
