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

public sealed record CommandRequestPayload(string CommandId);

public sealed record ContextSelectionRequestPayload(string Mode, string? ContextId);

public sealed record PingPayload;
