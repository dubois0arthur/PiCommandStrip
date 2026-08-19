using System.Text.Json;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.BrowserIntegration;

public static class BrowserIntegrationProtocol
{
    public const string Version = "3";
    public const int MaximumMessageSizeBytes = 8 * 1024;
    public const string HelloType = "browser_hello";
    public const string StateUpdateType = "browser_state_update";
    public const string CommandType = "browser_command";
    public const string CommandResultType = "browser_command_result";
}

public abstract record BrowserIntegrationClientMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc);

public sealed record BrowserHelloMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    string ProtocolVersion,
    string? AuthenticationToken,
    BrowserIdentity Identity) : BrowserIntegrationClientMessage(MessageId, TimestampUtc);

public sealed record BrowserStateUpdateMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    BrowserTabObservation Observation) : BrowserIntegrationClientMessage(MessageId, TimestampUtc);

public sealed record BrowserCommandResultMessage(
    Guid MessageId,
    DateTimeOffset TimestampUtc,
    BrowserExtensionCommandResult Result) : BrowserIntegrationClientMessage(MessageId, TimestampUtc);

public sealed record BrowserIntegrationParseError(
    string Code,
    string Message,
    Guid? RequestMessageId = null);

public sealed record BrowserIntegrationParseResult(
    BrowserIntegrationClientMessage? Message,
    BrowserIntegrationParseError? Error)
{
    public bool IsValid => Message is not null;

    public static BrowserIntegrationParseResult Success(BrowserIntegrationClientMessage message) =>
        new(message, null);

    public static BrowserIntegrationParseResult Failure(
        string code,
        string message,
        Guid? requestMessageId = null) =>
        new(null, new(code, message, requestMessageId));
}

public sealed class BrowserIntegrationMessageParser
{
    private const int MaximumTokenLength = 200;
    private const int MaximumIdentityLength = 128;
    private const int MaximumRawSelectedTextLength = 4096;

    public BrowserIntegrationParseResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json, ProtocolJson.DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactlyProperties(root, "type", "messageId", "timestampUtc", "payload") ||
                !TryString(root, "type", out var type) ||
                !TryGuid(root, "messageId", out var messageId) ||
                !TryUtc(root, "timestampUtc", out var timestampUtc) ||
                !root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return BrowserIntegrationParseResult.Failure(
                    "invalid_envelope",
                    "A valid browser bridge envelope is required.");
            }

            return type switch
            {
                BrowserIntegrationProtocol.HelloType =>
                    ParseHello(messageId, timestampUtc, payload),
                BrowserIntegrationProtocol.StateUpdateType =>
                    ParseStateUpdate(messageId, timestampUtc, payload),
                BrowserIntegrationProtocol.CommandResultType =>
                    ParseCommandResult(messageId, timestampUtc, payload),
                _ => BrowserIntegrationParseResult.Failure(
                    "unknown_message_type",
                    "The browser bridge message type is not supported.",
                    messageId)
            };
        }
        catch (JsonException)
        {
            return BrowserIntegrationParseResult.Failure(
                "malformed_json",
                "The message is not valid JSON.");
        }
    }

    private static BrowserIntegrationParseResult ParseCommandResult(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!HasExactlyProperties(payload, "requestMessageId", "succeeded", "code") ||
            !TryGuid(payload, "requestMessageId", out var requestMessageId) ||
            !TryRequiredBoolean(payload, "succeeded", out var succeeded) ||
            !TryBoundedString(payload, "code", 64, out var code))
        {
            return BrowserIntegrationParseResult.Failure(
                "invalid_payload",
                "The browser command result payload is invalid.",
                messageId);
        }

        return BrowserIntegrationParseResult.Success(new BrowserCommandResultMessage(
            messageId,
            timestampUtc,
            new BrowserExtensionCommandResult(requestMessageId, succeeded, code)));
    }

    private static BrowserIntegrationParseResult ParseHello(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!HasExactlyProperties(
                payload,
                "protocolVersion",
                "authenticationToken",
                "browserType",
                "sourceIdentifier",
                "instanceIdentifier") ||
            !TryString(payload, "protocolVersion", out var protocolVersion) ||
            !TryOptionalString(payload, "authenticationToken", MaximumTokenLength, out var token) ||
            !TryBoundedString(payload, "browserType", 32, out var browserType) ||
            !TryBoundedString(payload, "sourceIdentifier", MaximumIdentityLength, out var source) ||
            !TryBoundedString(payload, "instanceIdentifier", MaximumIdentityLength, out var instance))
        {
            return BrowserIntegrationParseResult.Failure(
                "invalid_payload",
                "The browser hello payload is invalid.",
                messageId);
        }

        return BrowserIntegrationParseResult.Success(new BrowserHelloMessage(
            messageId,
            timestampUtc,
            protocolVersion,
            token,
            new BrowserIdentity(browserType, source, instance)));
    }

    private static BrowserIntegrationParseResult ParseStateUpdate(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!HasExactlyProperties(
                payload,
                "activeTabId",
                "url",
                "title",
                "selectedText",
                "canGoBack",
                "canGoForward") ||
            !TryOptionalInt32(payload, "activeTabId", out var tabId) ||
            !TryOptionalString(payload, "url", BrowserStateNormalizer.MaximumUrlLength * 2, out var url) ||
            !TryOptionalString(payload, "title", BrowserStateNormalizer.MaximumTitleLength * 2, out var title) ||
            !TryOptionalString(payload, "selectedText", MaximumRawSelectedTextLength, out var selectedText) ||
            !TryOptionalBoolean(payload, "canGoBack", out var canGoBack) ||
            !TryOptionalBoolean(payload, "canGoForward", out var canGoForward))
        {
            return BrowserIntegrationParseResult.Failure(
                "invalid_payload",
                "The browser state payload is invalid.",
                messageId);
        }

        return BrowserIntegrationParseResult.Success(new BrowserStateUpdateMessage(
            messageId,
            timestampUtc,
            new BrowserTabObservation(tabId, url, title, selectedText, canGoBack, canGoForward)));
    }

    private static bool HasExactlyProperties(JsonElement value, params string[] expected)
    {
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == expected.Length &&
            expected.All(name => names.Count(candidate => candidate == name) == 1);
    }

    private static bool TryBoundedString(
        JsonElement value,
        string propertyName,
        int maximumLength,
        out string result) =>
        TryString(value, propertyName, out result) && result.Length <= maximumLength;

    private static bool TryString(JsonElement value, string propertyName, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static bool TryOptionalString(
        JsonElement value,
        string propertyName,
        int maximumLength,
        out string? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString();
        return result?.Length <= maximumLength;
    }

    private static bool TryOptionalInt32(
        JsonElement value,
        string propertyName,
        out int? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryOptionalBoolean(
        JsonElement value,
        string propertyName,
        out bool? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        result = property.GetBoolean();
        return true;
    }

    private static bool TryRequiredBoolean(
        JsonElement value,
        string propertyName,
        out bool result)
    {
        result = false;
        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        result = property.GetBoolean();
        return true;
    }

    private static bool TryGuid(JsonElement value, string propertyName, out Guid result)
    {
        result = default;
        return value.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out result);
    }

    private static bool TryUtc(
        JsonElement value,
        string propertyName,
        out DateTimeOffset result)
    {
        result = default;
        return value.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out result) &&
            result.Offset == TimeSpan.Zero;
    }
}
