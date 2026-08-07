using System.Security.Cryptography;

namespace PiCommandStrip.App.Authentication;

public enum ClientAuthenticationStatus
{
    Authenticated,
    Missing,
    Incorrect,
    Expired
}

public sealed class ClientAuthenticationService
{
    public static readonly TimeSpan MaximumAttemptAge = TimeSpan.FromMinutes(1);
    private const int RequiredTokenSizeBytes = 32;

    private readonly byte[] _expectedTokenHash;
    private readonly TimeProvider _timeProvider;

    public ClientAuthenticationService(string configuredToken, TimeProvider timeProvider)
    {
        Span<byte> tokenBytes = stackalloc byte[RequiredTokenSizeBytes];
        if (string.IsNullOrWhiteSpace(configuredToken) ||
            !Convert.TryFromBase64String(configuredToken, tokenBytes, out var bytesWritten) ||
            bytesWritten != RequiredTokenSizeBytes)
        {
            throw new InvalidOperationException(
                "PiCommandStrip:Authentication:Token must contain a randomly generated 32-byte Base64 token.");
        }

        _expectedTokenHash = SHA256.HashData(tokenBytes);
        CryptographicOperations.ZeroMemory(tokenBytes);
        _timeProvider = timeProvider;
    }

    public ClientAuthenticationStatus Authenticate(string? suppliedToken, DateTimeOffset attemptedAtUtc)
    {
        var age = _timeProvider.GetUtcNow() - attemptedAtUtc;
        if (age.Duration() > MaximumAttemptAge)
        {
            return ClientAuthenticationStatus.Expired;
        }

        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return ClientAuthenticationStatus.Missing;
        }

        Span<byte> suppliedBytes = stackalloc byte[RequiredTokenSizeBytes];
        if (!Convert.TryFromBase64String(suppliedToken, suppliedBytes, out var bytesWritten) ||
            bytesWritten != RequiredTokenSizeBytes)
        {
            CryptographicOperations.ZeroMemory(suppliedBytes);
            return ClientAuthenticationStatus.Incorrect;
        }

        Span<byte> suppliedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(suppliedBytes, suppliedHash);
        var matches = CryptographicOperations.FixedTimeEquals(_expectedTokenHash, suppliedHash);
        CryptographicOperations.ZeroMemory(suppliedBytes);
        CryptographicOperations.ZeroMemory(suppliedHash);

        return matches
            ? ClientAuthenticationStatus.Authenticated
            : ClientAuthenticationStatus.Incorrect;
    }
}
