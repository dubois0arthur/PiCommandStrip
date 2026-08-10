namespace PiCommandStrip.App.Protocol;

public abstract record ClientMessage(Guid MessageId, DateTimeOffset TimestampUtc);

public sealed record ClientHelloMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    ClientHelloPayload Payload) : ClientMessage(MessageId, TimestampUtc);

public sealed record CommandRequestMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    CommandRequestPayload Payload) : ClientMessage(MessageId, TimestampUtc);

public sealed record ContextSelectionRequestMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    ContextSelectionRequestPayload Payload) : ClientMessage(MessageId, TimestampUtc);

public sealed record PingMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    PingPayload Payload) : ClientMessage(MessageId, TimestampUtc);
