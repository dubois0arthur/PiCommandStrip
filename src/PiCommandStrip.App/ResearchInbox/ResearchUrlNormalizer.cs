namespace PiCommandStrip.App.ResearchInbox;

public static class ResearchUrlNormalizer
{
    public const int MaximumUrlLength = 4096;

    public static bool TryNormalize(string? value, out Uri? uri, out string normalizedUrl)
    {
        uri = null;
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumUrlLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        try
        {
            var builder = new UriBuilder(parsed)
            {
                Scheme = parsed.Scheme.ToLowerInvariant(),
                Host = parsed.IdnHost.ToLowerInvariant(),
                Fragment = string.Empty
            };
            if ((builder.Scheme == "http" && builder.Port == 80) ||
                (builder.Scheme == "https" && builder.Port == 443))
            {
                builder.Port = -1;
            }

            uri = builder.Uri;
            normalizedUrl = uri.AbsoluteUri;
            return normalizedUrl.Length <= MaximumUrlLength;
        }
        catch (UriFormatException)
        {
            uri = null;
            normalizedUrl = string.Empty;
            return false;
        }
    }
}

