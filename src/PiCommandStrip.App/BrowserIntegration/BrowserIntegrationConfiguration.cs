using System.Security.Cryptography;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed record BrowserIntegrationConfiguration(bool Enabled, int Port, string Token)
{
    private const int RequiredTokenSizeBytes = 32;

    public static BrowserIntegrationConfiguration Create(
        BrowserIntegrationOptions options,
        int dashboardPort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, 65535);

        if (options.Enabled && options.Port == dashboardPort)
        {
            throw new InvalidOperationException(
                "The browser integration port must differ from the dashboard port.");
        }

        if (!options.Enabled)
        {
            return new(false, options.Port, string.Empty);
        }

        Span<byte> tokenBytes = stackalloc byte[RequiredTokenSizeBytes];
        var valid = !string.IsNullOrWhiteSpace(options.Token) &&
            Convert.TryFromBase64String(options.Token, tokenBytes, out var bytesWritten) &&
            bytesWritten == RequiredTokenSizeBytes;
        CryptographicOperations.ZeroMemory(tokenBytes);

        if (!valid)
        {
            throw new InvalidOperationException(
                "PiCommandStrip:BrowserIntegration:Token must contain a separate randomly generated 32-byte Base64 token when browser integration is enabled.");
        }

        return new(true, options.Port, options.Token);
    }
}
