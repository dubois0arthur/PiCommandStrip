using System.Text.Json;

namespace PiCommandStrip.App.Protocol;

public sealed class ClientMessageParser
{
    private const int MaximumClientNameLength = 100;
    private const int MaximumCommandIdLength = 100;

    public ProtocolParseResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json, ProtocolJson.DocumentOptions);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                return ProtocolParseResult.Failure("invalid_envelope", "The message must be a JSON object.");
            }

            if (!TryGetRequiredString(root, "type", out var type))
            {
                return ProtocolParseResult.Failure("invalid_envelope", "A non-empty string property named 'type' is required.");
            }

            if (!TryGetRequiredGuid(root, "messageId", out var messageId))
            {
                return ProtocolParseResult.Failure("invalid_envelope", "A non-empty GUID property named 'messageId' is required.");
            }

            if (!TryGetRequiredUtcTimestamp(root, "timestampUtc", out var timestampUtc))
            {
                return ProtocolParseResult.Failure(
                    "invalid_envelope",
                    "A valid UTC timestamp property named 'timestampUtc' is required.",
                    messageId);
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind is not JsonValueKind.Object)
            {
                return ProtocolParseResult.Failure(
                    "invalid_envelope",
                    "An object property named 'payload' is required.",
                    messageId);
            }

            return type switch
            {
                MessageTypes.ClientHello => ParseClientHello(messageId, timestampUtc, payload),
                MessageTypes.CommandRequest => ParseCommandRequest(messageId, timestampUtc, payload),
                MessageTypes.Ping => ProtocolParseResult.Success(
                    new PingMessage(messageId, timestampUtc, new PingPayload())),
                _ => ProtocolParseResult.Failure(
                    "unknown_message_type",
                    $"The client message type '{type}' is not supported.",
                    messageId)
            };
        }
        catch (JsonException)
        {
            return ProtocolParseResult.Failure("malformed_json", "The message is not valid JSON.");
        }
    }

    private static ProtocolParseResult ParseClientHello(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!TryGetRequiredString(payload, "clientName", out var clientName) ||
            clientName.Length > MaximumClientNameLength)
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                $"'clientName' is required and must not exceed {MaximumClientNameLength} characters.",
                messageId);
        }

        if (!TryGetRequiredString(payload, "protocolVersion", out var protocolVersion))
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                "'protocolVersion' is required.",
                messageId);
        }

        return ProtocolParseResult.Success(new ClientHelloMessage(
            messageId,
            timestampUtc,
            new ClientHelloPayload(clientName, protocolVersion)));
    }

    private static ProtocolParseResult ParseCommandRequest(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!TryGetRequiredString(payload, "commandId", out var commandId) ||
            commandId.Length > MaximumCommandIdLength)
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                $"'commandId' is required and must not exceed {MaximumCommandIdLength} characters.",
                messageId);
        }

        return ProtocolParseResult.Success(new CommandRequestMessage(
            messageId,
            timestampUtc,
            new CommandRequestPayload(commandId)));
    }

    private static bool TryGetRequiredString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;

        if (!parent.TryGetProperty(name, out var property) || property.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryGetRequiredGuid(JsonElement parent, string name, out Guid value)
    {
        value = Guid.Empty;

        return parent.TryGetProperty(name, out var property) &&
            property.ValueKind is JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out value) &&
            value != Guid.Empty;
    }

    private static bool TryGetRequiredUtcTimestamp(
        JsonElement parent,
        string name,
        out DateTimeOffset value)
    {
        value = default;

        return parent.TryGetProperty(name, out var property) &&
            property.ValueKind is JsonValueKind.String &&
            property.TryGetDateTimeOffset(out value) &&
            value.Offset == TimeSpan.Zero;
    }
}
