namespace PiCommandStrip.App.Protocol;

public sealed record ServerHelloPayload(
    string ApplicationName,
    string ProtocolVersion,
    int MaximumMessageSizeBytes);

public sealed record PcStatePayload(string ProcessName, string WindowTitle);

public sealed record CommandResultPayload(
    Guid RequestMessageId,
    string CommandId,
    bool Succeeded,
    string Message);

public sealed record PongPayload(Guid RequestMessageId);

public sealed record ErrorPayload(Guid? RequestMessageId, string Code, string Message);

public sealed record ClientHelloPayload(string ClientName, string ProtocolVersion);

public sealed record CommandRequestPayload(string CommandId);

public sealed record PingPayload;
