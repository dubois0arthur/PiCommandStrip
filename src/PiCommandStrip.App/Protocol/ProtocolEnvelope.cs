namespace PiCommandStrip.App.Protocol;

public sealed record ProtocolEnvelope<TPayload>(
    string Type,
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    TPayload Payload);
