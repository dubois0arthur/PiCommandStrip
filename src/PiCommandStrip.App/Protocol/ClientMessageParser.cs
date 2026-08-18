using System.Text.Json;
using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.App.Protocol;

public sealed class ClientMessageParser
{
    private const int MaximumClientNameLength = 100;
    private const int MaximumCommandIdLength = 100;
    private const int MaximumContextIdLength = 50;
    private const int MaximumAuthenticationTokenLength = 200;

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
                MessageTypes.ContextSelectionRequest =>
                    ParseContextSelectionRequest(messageId, timestampUtc, payload),
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

        string? authenticationToken = null;
        if (payload.TryGetProperty("authenticationToken", out var authenticationProperty))
        {
            if (authenticationProperty.ValueKind is JsonValueKind.Null)
            {
                authenticationToken = null;
            }
            else if (authenticationProperty.ValueKind is not JsonValueKind.String)
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    "'authenticationToken' must be a string when provided.",
                    messageId);
            }

            else
            {
                authenticationToken = authenticationProperty.GetString();
            }
            if (authenticationToken?.Length > MaximumAuthenticationTokenLength)
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    $"'authenticationToken' must not exceed {MaximumAuthenticationTokenLength} characters.",
                    messageId);
            }
        }

        return ProtocolParseResult.Success(new ClientHelloMessage(
            messageId,
            timestampUtc,
            new ClientHelloPayload(clientName, protocolVersion, authenticationToken)));
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

        if (commandId == PcCommandIds.MediaSeek)
        {
            if (!HasExactlyProperties(payload, "commandId", "positionMilliseconds") ||
                !TryGetRequiredInt64(payload, "positionMilliseconds", out var positionMilliseconds) ||
                positionMilliseconds < 0 ||
                positionMilliseconds > MediaCommandHandler.MaximumSeekPositionMilliseconds)
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    $"'{PcCommandIds.MediaSeek}' requires only 'commandId' and a non-negative integer 'positionMilliseconds'.",
                    messageId);
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, positionMilliseconds)));
        }

        if (commandId == PcCommandIds.AudioSetMasterVolume)
        {
            if (!HasExactlyProperties(payload, "commandId", "volume") ||
                !TryGetRequiredVolume(payload, "volume", out var volume))
            {
                return InvalidAudioPayload(
                    messageId,
                    $"'{commandId}' requires only 'commandId' and a numeric 'volume' from 0 through 1.");
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, Volume: volume)));
        }

        if (commandId == PcCommandIds.AudioSetMasterMute)
        {
            if (!HasExactlyProperties(payload, "commandId", "isMuted") ||
                !TryGetRequiredBoolean(payload, "isMuted", out var isMuted))
            {
                return InvalidAudioPayload(
                    messageId,
                    $"'{commandId}' requires only 'commandId' and a Boolean 'isMuted'.");
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, IsMuted: isMuted)));
        }

        if (commandId == PcCommandIds.AudioSetApplicationVolume)
        {
            if (!HasExactlyProperties(payload, "commandId", "applicationId", "volume") ||
                !TryGetApplicationId(payload, out var applicationId) ||
                !TryGetRequiredVolume(payload, "volume", out var volume))
            {
                return InvalidAudioPayload(
                    messageId,
                    $"'{commandId}' requires only a valid 'applicationId' and a numeric 'volume' from 0 through 1.");
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(
                    commandId,
                    ApplicationId: applicationId,
                    Volume: volume)));
        }

        if (commandId == PcCommandIds.AudioSetApplicationMute)
        {
            if (!HasExactlyProperties(payload, "commandId", "applicationId", "isMuted") ||
                !TryGetApplicationId(payload, out var applicationId) ||
                !TryGetRequiredBoolean(payload, "isMuted", out var isMuted))
            {
                return InvalidAudioPayload(
                    messageId,
                    $"'{commandId}' requires only a valid 'applicationId' and a Boolean 'isMuted'.");
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(
                    commandId,
                    ApplicationId: applicationId,
                    IsMuted: isMuted)));
        }

        if (commandId == PcCommandIds.AudioSetOutputDevice)
        {
            if (!HasExactlyProperties(payload, "commandId", "deviceId") ||
                !TryGetRequiredString(payload, "deviceId", out var deviceId) ||
                !AudioOutputDeviceTargetResolver.IsValidDeviceIdShape(deviceId))
            {
                return InvalidAudioPayload(
                    messageId,
                    $"'{commandId}' requires only 'commandId' and a non-empty bounded 'deviceId'.");
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, DeviceId: deviceId)));
        }

        if (commandId == PcCommandIds.SpotifySetSaved)
        {
            if (!HasExactlyProperties(payload, "commandId", "isSaved") ||
                !TryGetRequiredBoolean(payload, "isSaved", out var isSaved))
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    $"'{commandId}' requires only 'commandId' and a Boolean 'isSaved'.",
                    messageId);
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, IsSaved: isSaved)));
        }

        if (commandId == PcCommandIds.SpotifySetShuffle)
        {
            if (!HasExactlyProperties(payload, "commandId", "shuffleEnabled") ||
                !TryGetRequiredBoolean(payload, "shuffleEnabled", out var shuffleEnabled))
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    $"'{commandId}' requires only 'commandId' and a Boolean 'shuffleEnabled'.",
                    messageId);
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, ShuffleEnabled: shuffleEnabled)));
        }

        if (commandId == PcCommandIds.SpotifySetRepeat)
        {
            if (!HasExactlyProperties(payload, "commandId", "repeatState") ||
                !TryGetRequiredString(payload, "repeatState", out var repeatState) ||
                !SpotifyRepeatStates.IsValid(repeatState))
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    $"'{commandId}' requires only 'commandId' and 'repeatState' as 'off', 'context', or 'track'.",
                    messageId);
            }

            return ProtocolParseResult.Success(new CommandRequestMessage(
                messageId,
                timestampUtc,
                new CommandRequestPayload(commandId, RepeatState: repeatState)));
        }

        if (!HasExactlyOneProperty(payload, "commandId"))
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                "This command payload may contain only 'commandId'.",
                messageId);
        }

        return ProtocolParseResult.Success(new CommandRequestMessage(
            messageId,
            timestampUtc,
            new CommandRequestPayload(commandId)));
    }

    private static ProtocolParseResult ParseContextSelectionRequest(
        Guid messageId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        if (!TryGetRequiredString(payload, "mode", out var mode))
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                "'mode' is required.",
                messageId);
        }

        if (mode == ContextSelectionModes.Automatic)
        {
            if (!HasExactlyOneProperty(payload, "mode"))
            {
                return ProtocolParseResult.Failure(
                    "invalid_payload",
                    "Automatic context selection may contain only 'mode'.",
                    messageId);
            }

            return ProtocolParseResult.Success(new ContextSelectionRequestMessage(
                messageId,
                timestampUtc,
                new ContextSelectionRequestPayload(mode, null)));
        }

        if (mode != ContextSelectionModes.Manual ||
            !HasExactlyProperties(payload, "mode", "contextId") ||
            !TryGetRequiredString(payload, "contextId", out var contextId) ||
            contextId.Length > MaximumContextIdLength)
        {
            return ProtocolParseResult.Failure(
                "invalid_payload",
                $"Manual context selection requires only 'mode' and a non-empty 'contextId' of at most {MaximumContextIdLength} characters.",
                messageId);
        }

        return ProtocolParseResult.Success(new ContextSelectionRequestMessage(
            messageId,
            timestampUtc,
            new ContextSelectionRequestPayload(mode, contextId)));
    }

    private static bool HasExactlyOneProperty(JsonElement parent, string expectedName)
    {
        var propertyCount = 0;

        foreach (var property in parent.EnumerateObject())
        {
            propertyCount++;

            if (!property.NameEquals(expectedName))
            {
                return false;
            }
        }

        return propertyCount == 1;
    }

    private static bool HasExactlyProperties(
        JsonElement parent,
        params string[] expectedNames)
    {
        if (expectedNames.Length == 0)
        {
            return !parent.EnumerateObject().Any();
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in parent.EnumerateObject())
        {
            if (!expectedNames.Contains(property.Name, StringComparer.Ordinal) ||
                !found.Add(property.Name))
            {
                return false;
            }
        }

        return found.Count == expectedNames.Length;
    }

    private static ProtocolParseResult InvalidAudioPayload(Guid messageId, string message) =>
        ProtocolParseResult.Failure("invalid_payload", message, messageId);

    private static bool TryGetApplicationId(JsonElement payload, out string applicationId)
    {
        if (!TryGetRequiredString(payload, "applicationId", out applicationId) ||
            !AudioMixerTargetResolver.IsValidApplicationId(applicationId))
        {
            applicationId = string.Empty;
            return false;
        }

        return true;
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

    private static bool TryGetRequiredInt64(JsonElement parent, string name, out long value)
    {
        value = default;

        return parent.TryGetProperty(name, out var property) &&
            property.ValueKind is JsonValueKind.Number &&
            property.TryGetInt64(out value);
    }

    private static bool TryGetRequiredVolume(
        JsonElement parent,
        string name,
        out float value)
    {
        value = default;

        return parent.TryGetProperty(name, out var property) &&
            property.ValueKind is JsonValueKind.Number &&
            property.TryGetSingle(out value) &&
            float.IsFinite(value) &&
            value is >= 0 and <= 1;
    }

    private static bool TryGetRequiredBoolean(
        JsonElement parent,
        string name,
        out bool value)
    {
        value = default;

        if (!parent.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
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
