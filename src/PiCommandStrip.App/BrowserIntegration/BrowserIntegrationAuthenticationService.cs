using System.Security.Cryptography;

namespace PiCommandStrip.App.BrowserIntegration;

public enum BrowserAuthenticationStatus
{
    Authenticated,
    Missing,
    Incorrect,
    Expired
}

public sealed class BrowserIntegrationAuthenticationService
{
    public static readonly TimeSpan MaximumAttemptAge = TimeSpan.FromMinutes(1);
    private const int RequiredTokenSizeBytes = 32;
    private readonly byte[] _expectedTokenHash;
    private readonly TimeProvider _timeProvider;

    public BrowserIntegrationAuthenticationService(
        BrowserIntegrationConfiguration configuration,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        if (!configuration.Enabled)
        {
            _expectedTokenHash = [];
            return;
        }

        Span<byte> tokenBytes = stackalloc byte[RequiredTokenSizeBytes];
        Convert.TryFromBase64String(configuration.Token, tokenBytes, out _);
        _expectedTokenHash = SHA256.HashData(tokenBytes);
        CryptographicOperations.ZeroMemory(tokenBytes);
    }

    public BrowserAuthenticationStatus Authenticate(
        string? suppliedToken,
        DateTimeOffset attemptedAtUtc)
    {
        if ((_timeProvider.GetUtcNow() - attemptedAtUtc).Duration() > MaximumAttemptAge)
        {
            return BrowserAuthenticationStatus.Expired;
        }

        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return BrowserAuthenticationStatus.Missing;
        }

        Span<byte> suppliedBytes = stackalloc byte[RequiredTokenSizeBytes];
        if (!Convert.TryFromBase64String(suppliedToken, suppliedBytes, out var bytesWritten) ||
            bytesWritten != RequiredTokenSizeBytes)
        {
            CryptographicOperations.ZeroMemory(suppliedBytes);
            return BrowserAuthenticationStatus.Incorrect;
        }

        Span<byte> suppliedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(suppliedBytes, suppliedHash);
        var matches = CryptographicOperations.FixedTimeEquals(_expectedTokenHash, suppliedHash);
        CryptographicOperations.ZeroMemory(suppliedBytes);
        CryptographicOperations.ZeroMemory(suppliedHash);
        return matches
            ? BrowserAuthenticationStatus.Authenticated
            : BrowserAuthenticationStatus.Incorrect;
    }
}
