namespace PiCommandStrip.App.Protocol;

public sealed record ServerHelloPayload(
    string ApplicationName,
    string ProtocolVersion,
    int MaximumMessageSizeBytes,
    IReadOnlyList<ContextDescriptorPayload> AvailableContexts);

public sealed record ContextDescriptorPayload(string ContextId, string DisplayName);

public sealed record PcStatePayload(
    bool IsAvailable,
    string? ProcessName,
    int? ProcessId,
    string? WindowTitle,
    DateTimeOffset ObservedAtUtc);

public sealed record ContextStatePayload(
    string ContextId,
    string DisplayName,
    string SelectionMode,
    string Source,
    string Trigger,
    string? ForegroundProcess,
    string? ForegroundWindowTitle,
    DateTimeOffset ActiveSinceUtc);

public sealed record MediaStatePayload(
    bool HasActiveSession,
    string? SessionSourceIdentifier,
    string? SourceName,
    string? Title,
    string? Artist,
    string? AlbumTitle,
    string PlaybackState,
    long? PositionMilliseconds,
    long? TotalDurationMilliseconds,
    bool SupportsPrevious,
    bool SupportsNext,
    bool SupportsPlay,
    bool SupportsPause,
    bool SupportsPlayPause,
    bool SupportsSeeking,
    DateTimeOffset LastUpdatedUtc,
    string? ArtworkUrl);

public sealed record SpotifyStatePayload(
    string Status,
    bool IsConfigured,
    bool IsAuthenticated,
    bool AppliesToCurrentMedia,
    string? ItemType,
    bool? IsSaved,
    bool? ShuffleEnabled,
    string? RepeatState,
    SpotifyDevicePayload? Device,
    IReadOnlyList<SpotifyQueueItemPayload> Queue,
    DateTimeOffset LastUpdatedUtc,
    DateTimeOffset? RetryAfterUtc);

public sealed record SpotifyDevicePayload(
    string Name,
    string Type,
    bool IsRestricted);

public sealed record SpotifyQueueItemPayload(
    string Title,
    string? Subtitle,
    string ItemType);

public sealed record AudioStatePayload(
    bool IsAvailable,
    AudioOutputDevicePayload? OutputDevice,
    IReadOnlyList<AudioOutputDeviceDescriptorPayload> OutputDevices,
    IReadOnlyList<ApplicationAudioPayload> Applications,
    long Revision,
    DateTimeOffset LastUpdatedUtc);

public sealed record AudioOutputDevicePayload(
    string DeviceId,
    string FriendlyName,
    float Volume,
    bool IsMuted);

public sealed record AudioOutputDeviceDescriptorPayload(
    string DeviceId,
    string FriendlyName,
    string State,
    bool IsDefault);

public sealed record ApplicationAudioPayload(
    string ApplicationId,
    IReadOnlyList<int> ProcessIds,
    string? ProcessName,
    string DisplayName,
    float Volume,
    bool IsMuted,
    string State,
    int SessionCount,
    bool HasMixedVolume,
    bool HasMixedMute);

public sealed record BrowserStatePayload(
    string ConnectionState,
    string? BrowserType,
    string? SourceIdentifier,
    string? InstanceIdentifier,
    int? ActiveTabId,
    string? Url,
    string? HostName,
    string? PageTitle,
    bool HasSelectedText,
    bool? CanGoBack,
    bool? CanGoForward,
    DateTimeOffset LastUpdatedUtc);

public sealed record ContextSelectionResultPayload(
    Guid RequestMessageId,
    bool Succeeded,
    string Message,
    DateTimeOffset CompletedAtUtc);

public sealed record CommandResultPayload(
    Guid RequestMessageId,
    string CommandId,
    bool Succeeded,
    string Message,
    DateTimeOffset CompletedAtUtc);

public sealed record PongPayload(Guid RequestMessageId);

public sealed record ErrorPayload(Guid? RequestMessageId, string Code, string Message);

public sealed record ClientHelloPayload(
    string ClientName,
    string ProtocolVersion,
    string? AuthenticationToken);

public sealed record CommandRequestPayload(
    string CommandId,
    long? PositionMilliseconds = null,
    string? ApplicationId = null,
    float? Volume = null,
    bool? IsMuted = null,
    string? DeviceId = null,
    bool? IsSaved = null,
    bool? ShuffleEnabled = null,
    string? RepeatState = null);

public sealed record ContextSelectionRequestPayload(string Mode, string? ContextId);

public sealed record PingPayload;
