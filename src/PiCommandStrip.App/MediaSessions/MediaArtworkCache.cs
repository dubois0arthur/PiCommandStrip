using System.Security.Cryptography;

namespace PiCommandStrip.App.MediaSessions;

public sealed record MediaArtworkContent(string Id, string ContentType, byte[] Bytes);

public interface IMediaArtworkCache
{
    string? Store(byte[] bytes, string? contentType);

    bool TryGet(string artworkId, out MediaArtworkContent? artwork);

    void Clear();
}

public sealed class MediaArtworkCache : IMediaArtworkCache
{
    public const int MaximumArtworkBytes = 5 * 1024 * 1024;
    public const int ArtworkIdLength = SHA256.HashSizeInBytes * 2;
    public const string RoutePrefix = "/media/artwork/";

    private readonly Lock _sync = new();
    private MediaArtworkContent? _current;

    public string? Store(byte[] bytes, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var normalizedContentType = NormalizeContentType(contentType, bytes);
        if (bytes.Length is 0 || bytes.Length > MaximumArtworkBytes || normalizedContentType is null)
        {
            Clear();
            return null;
        }

        var id = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var content = new MediaArtworkContent(id, normalizedContentType, [.. bytes]);

        lock (_sync)
        {
            _current = content;
        }

        return id;
    }

    public bool TryGet(string artworkId, out MediaArtworkContent? artwork)
    {
        if (!IsValidArtworkId(artworkId))
        {
            artwork = null;
            return false;
        }

        lock (_sync)
        {
            if (_current is not null &&
                string.Equals(_current.Id, artworkId, StringComparison.Ordinal))
            {
                artwork = _current;
                return true;
            }
        }

        artwork = null;
        return false;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }
    }

    public static string CreateUrl(string artworkId)
    {
        if (!IsValidArtworkId(artworkId))
        {
            throw new ArgumentException("Artwork IDs must be lowercase SHA-256 values.", nameof(artworkId));
        }

        return $"{RoutePrefix}{artworkId}";
    }

    public static bool IsValidArtworkId(string? artworkId) =>
        artworkId is { Length: ArtworkIdLength } &&
        artworkId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? NormalizeContentType(string? contentType, ReadOnlySpan<byte> bytes)
    {
        var value = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        var declaredType = value switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            "image/gif" => "image/gif",
            "image/bmp" => "image/bmp",
            _ => null
        };

        return declaredType ?? (value is null or "" or "application/octet-stream"
            ? DetectContentType(bytes)
            : null);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        return bytes.StartsWith("BM"u8) ? "image/bmp" : null;
    }
}
