using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PiCommandStrip.App.Spotify;

public sealed class ProtectedSpotifyTokenStore(
    IDataProtectionProvider dataProtectionProvider) : ISpotifyTokenStore
{
    private const string FileName = "spotify-authorization.v1";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "PiCommandStrip.Spotify.Authorization.v1");
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PiCommandStrip",
        FileName);

    public async Task<StoredSpotifyAuthorization?> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var protectedPayload = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var json = _protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<StoredSpotifyAuthorization>(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        StoredSpotifyAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Spotify token storage path has no directory.");
        Directory.CreateDirectory(directory);
        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(authorization));
        var temporaryPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, protectedPayload, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}
