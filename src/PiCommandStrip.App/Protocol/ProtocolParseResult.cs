namespace PiCommandStrip.App.Protocol;

public sealed record ProtocolValidationError(string Code, string Message, Guid? RequestMessageId = null);

public sealed record ProtocolParseResult(ClientMessage? Message, ProtocolValidationError? Error)
{
    public bool IsValid => Message is not null;

    public static ProtocolParseResult Success(ClientMessage message) => new(message, null);

    public static ProtocolParseResult Failure(
        string code,
        string message,
        Guid? requestMessageId = null) =>
        new(null, new ProtocolValidationError(code, message, requestMessageId));
}
