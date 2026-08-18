using System.Text;
using PiCommandStrip.App.MediaSessions;

namespace PiCommandStrip.App.Spotify;

public static class SpotifyMediaMatcher
{
    public static bool IsSpotifySource(MediaState mediaState) =>
        mediaState.HasActiveSession &&
        (ContainsSpotifyIdentity(mediaState.SourceName) ||
            ContainsSpotifyIdentity(mediaState.SessionSourceIdentifier));

    public static bool MatchesCurrentItem(
        MediaState mediaState,
        SpotifyPlaybackSnapshot playback)
    {
        if (!IsSpotifySource(mediaState) ||
            string.IsNullOrWhiteSpace(playback.ItemUri) ||
            string.IsNullOrWhiteSpace(mediaState.Title) ||
            string.IsNullOrWhiteSpace(playback.Title))
        {
            return false;
        }

        return NormalizeComparable(mediaState.Title) == NormalizeComparable(playback.Title);
    }

    private static bool ContainsSpotifyIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value
            .Split(['.', '!', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals("spotify", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("spotifyab", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("spotifymusic", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeComparable(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
