namespace PiCommandStrip.App.Spotify;

public static class SpotifyStatuses
{
    public const string Unconfigured = "unconfigured";
    public const string Unauthenticated = "unauthenticated";
    public const string Idle = "idle";
    public const string Available = "available";
    public const string RateLimited = "rate_limited";
    public const string Error = "error";
}

public static class SpotifyRepeatStates
{
    public const string Off = "off";
    public const string Context = "context";
    public const string Track = "track";

    public static bool IsValid(string? value) =>
        value is Off or Context or Track;
}

public sealed record SpotifyDeviceState(
    string Name,
    string Type,
    bool IsRestricted);

public sealed record SpotifyQueueItemState(
    string Title,
    string? Subtitle,
    string ItemType);

public sealed record SpotifyState(
    string Status,
    bool IsConfigured,
    bool IsAuthenticated,
    bool AppliesToCurrentMedia,
    string? ItemUri,
    string? ItemType,
    string? MatchedMediaTitle,
    bool? IsSaved,
    bool? ShuffleEnabled,
    string? RepeatState,
    SpotifyDeviceState? Device,
    IReadOnlyList<SpotifyQueueItemState> Queue,
    DateTimeOffset LastUpdatedUtc,
    DateTimeOffset? RetryAfterUtc = null)
{
    public static SpotifyState Unconfigured(DateTimeOffset timestamp) =>
        new(
            SpotifyStatuses.Unconfigured,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            timestamp);

    public static SpotifyState Unauthenticated(DateTimeOffset timestamp) =>
        new(
            SpotifyStatuses.Unauthenticated,
            true,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            timestamp);

    public static SpotifyState Idle(bool authenticated, DateTimeOffset timestamp) =>
        new(
            SpotifyStatuses.Idle,
            true,
            authenticated,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            timestamp);

    public bool HasSameMeaningAs(SpotifyState other) =>
        Status == other.Status &&
        IsConfigured == other.IsConfigured &&
        IsAuthenticated == other.IsAuthenticated &&
        AppliesToCurrentMedia == other.AppliesToCurrentMedia &&
        ItemUri == other.ItemUri &&
        ItemType == other.ItemType &&
        MatchedMediaTitle == other.MatchedMediaTitle &&
        IsSaved == other.IsSaved &&
        ShuffleEnabled == other.ShuffleEnabled &&
        RepeatState == other.RepeatState &&
        Device == other.Device &&
        Nullable.Equals(RetryAfterUtc, other.RetryAfterUtc) &&
        Queue.SequenceEqual(other.Queue);
}
