using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiCommandStrip.App.Spotify;

public sealed class SpotifyWebApiClient(
    HttpClient httpClient,
    SpotifyConfiguration configuration) : ISpotifyApiClient
{
    private static readonly Uri TokenEndpoint = new("https://accounts.spotify.com/api/token");
    private static readonly Uri ApiRoot = new("https://api.spotify.com/v1/");

    public Task<SpotifyTokenResponse> ExchangeAuthorizationCodeAsync(
        string code,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(
            "exchange authorization code",
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri
            },
            null,
            cancellationToken);

    public Task<SpotifyTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(
            "refresh access token",
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            refreshToken,
            cancellationToken);

    public async Task<SpotifyPlaybackSnapshot?> GetPlaybackAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(
            HttpMethod.Get,
            "me/player?additional_types=track%2Cepisode",
            accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "read playback", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("item", out var item) || item.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        var itemType = ReadString(item, "type");
        var subtitle = itemType == "track"
            ? ReadFirstArtist(item)
            : item.TryGetProperty("show", out var show)
                ? ReadString(show, "name")
                : null;
        return new SpotifyPlaybackSnapshot(
            ReadString(item, "uri"),
            itemType,
            ReadString(item, "name"),
            subtitle,
            ReadBoolean(root, "shuffle_state"),
            ReadString(root, "repeat_state") ?? SpotifyRepeatStates.Off,
            ReadDevice(root));
    }

    public async Task<bool> IsSavedAsync(
        string accessToken,
        string itemUri,
        CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(
            HttpMethod.Get,
            $"me/library/contains?uris={Uri.EscapeDataString(itemUri)}",
            accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "check saved item", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var values = await JsonSerializer.DeserializeAsync<bool[]>(stream, cancellationToken: cancellationToken);
        return values is [var saved, ..] && saved;
    }

    public async Task SetSavedAsync(
        string accessToken,
        string itemUri,
        bool isSaved,
        CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(
            isSaved ? HttpMethod.Put : HttpMethod.Delete,
            $"me/library?uris={Uri.EscapeDataString(itemUri)}",
            accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(
            response,
            isSaved ? "save item" : "remove saved item",
            cancellationToken);
    }

    public async Task<SpotifyQueueSnapshot> GetQueueAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(HttpMethod.Get, "me/player/queue", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "read queue", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("queue", out var queue) ||
            queue.ValueKind is not JsonValueKind.Array)
        {
            return new SpotifyQueueSnapshot([]);
        }

        var items = queue.EnumerateArray()
            .Take(5)
            .Select(item => new SpotifyQueueItemState(
                ReadString(item, "name") ?? "Untitled item",
                ReadString(item, "type") == "track"
                    ? ReadFirstArtist(item)
                    : item.TryGetProperty("show", out var show)
                        ? ReadString(show, "name")
                        : null,
                ReadString(item, "type") ?? "unknown"))
            .ToArray();
        return new SpotifyQueueSnapshot(items);
    }

    public Task SetShuffleAsync(
        string accessToken,
        bool enabled,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Put,
            $"me/player/shuffle?state={enabled.ToString().ToLowerInvariant()}",
            accessToken,
            "set shuffle",
            cancellationToken);

    public Task SetRepeatAsync(
        string accessToken,
        string repeatState,
        CancellationToken cancellationToken) =>
        SendWithoutResponseAsync(
            HttpMethod.Put,
            $"me/player/repeat?state={Uri.EscapeDataString(repeatState)}",
            accessToken,
            "set repeat",
            cancellationToken);

    private async Task<SpotifyTokenResponse> RequestTokenAsync(
        string operation,
        IReadOnlyDictionary<string, string> fields,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{configuration.ClientId}:{configuration.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, operation, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TokenPayload>(
            stream,
            cancellationToken: cancellationToken)
            ?? throw new SpotifyApiException(operation, HttpStatusCode.BadGateway);
        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new SpotifyApiException(operation, HttpStatusCode.BadGateway);
        }

        return new SpotifyTokenResponse(
            payload.AccessToken,
            string.IsNullOrWhiteSpace(payload.RefreshToken)
                ? existingRefreshToken ?? string.Empty
                : payload.RefreshToken,
            payload.Scope ?? string.Empty,
            payload.ExpiresIn);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string relativePath,
        string accessToken,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(method, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, operation, cancellationToken);
    }

    private static HttpRequestMessage CreateApiRequest(
        HttpMethod method,
        string relativePath,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, new Uri(ApiRoot, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await response.Content.LoadIntoBufferAsync(cancellationToken);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null &&
            response.Headers.TryGetValues("Retry-After", out var values) &&
            int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            retryAfter = TimeSpan.FromSeconds(Math.Max(1, seconds));
        }

        throw new SpotifyApiException(operation, response.StatusCode, retryAfter);
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? ReadFirstArtist(JsonElement item) =>
        item.TryGetProperty("artists", out var artists) &&
        artists.ValueKind == JsonValueKind.Array &&
        artists.GetArrayLength() > 0
            ? ReadString(artists[0], "name")
            : null;

    private static SpotifyDeviceState? ReadDevice(JsonElement root)
    {
        if (!root.TryGetProperty("device", out var device) ||
            device.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var name = ReadString(device, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new SpotifyDeviceState(
            name,
            ReadString(device, "type") ?? "unknown",
            ReadBoolean(device, "is_restricted"));
    }

    private sealed record TokenPayload(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
