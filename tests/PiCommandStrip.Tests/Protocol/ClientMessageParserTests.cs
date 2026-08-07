using System.Text.Json;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.Tests.Protocol;

public sealed class ClientMessageParserTests
{
    private static readonly Guid MessageId = Guid.Parse("4de31b91-9a89-42cb-a35f-3a36ac38dd5f");
    private static readonly DateTimeOffset TimestampUtc =
        new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly ClientMessageParser _parser = new();

    [Fact]
    public void Parse_ValidClientHello_ReturnsStronglyTypedMessage()
    {
        var json = CreateEnvelope(MessageTypes.ClientHello, new
        {
            clientName = "test-dashboard",
            protocolVersion = ProtocolConstants.Version,
            authenticationToken = "test-token"
        });

        var result = _parser.Parse(json);

        var message = Assert.IsType<ClientHelloMessage>(result.Message);
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(MessageId, message.MessageId);
        Assert.Equal(TimestampUtc, message.TimestampUtc);
        Assert.Equal("test-dashboard", message.Payload.ClientName);
        Assert.Equal(ProtocolConstants.Version, message.Payload.ProtocolVersion);
        Assert.Equal("test-token", message.Payload.AuthenticationToken);
    }

    [Fact]
    public void Parse_ValidPing_ReturnsPingMessage()
    {
        var json = CreateEnvelope(MessageTypes.Ping, new { });

        var result = _parser.Parse(json);

        var message = Assert.IsType<PingMessage>(result.Message);
        Assert.Equal(MessageId, message.MessageId);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_ValidCommandRequest_ProducesDataOnlyTransportMessage()
    {
        var json = CreateEnvelope(MessageTypes.CommandRequest, new
        {
            commandId = "demo.safe-command"
        });

        var result = _parser.Parse(json);

        var message = Assert.IsType<CommandRequestMessage>(result.Message);
        Assert.Equal("demo.safe-command", message.Payload.CommandId);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsValidationError()
    {
        var result = _parser.Parse("{"u8.ToArray());

        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.Equal("malformed_json", result.Error?.Code);
    }

    [Fact]
    public void Parse_UnknownMessageType_ReturnsCorrelatedValidationError()
    {
        var json = CreateEnvelope("run_anything", new { });

        var result = _parser.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal("unknown_message_type", result.Error?.Code);
        Assert.Equal(MessageId, result.Error?.RequestMessageId);
    }

    [Fact]
    public void Parse_MissingTimestamp_ReturnsInvalidEnvelopeError()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = MessageTypes.Ping,
            messageId = MessageId,
            payload = new { }
        });

        var result = _parser.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_envelope", result.Error?.Code);
        Assert.Equal(MessageId, result.Error?.RequestMessageId);
    }

    [Fact]
    public void Parse_EmptyCommandId_ReturnsInvalidPayloadError()
    {
        var json = CreateEnvelope(MessageTypes.CommandRequest, new
        {
            commandId = "   "
        });

        var result = _parser.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
        Assert.Equal(MessageId, result.Error?.RequestMessageId);
    }

    [Fact]
    public void Parse_CommandPayloadWithExecutablePath_ReturnsInvalidPayloadError()
    {
        var json = CreateEnvelope(MessageTypes.CommandRequest, new
        {
            commandId = PiCommandStrip.App.PcCommands.PcCommandIds.OpenNotepad,
            executablePath = @"C:\Windows\System32\notepad.exe"
        });

        var result = _parser.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
        Assert.Equal(MessageId, result.Error?.RequestMessageId);
    }

    [Fact]
    public void Parse_NonUtcTimestamp_ReturnsInvalidEnvelopeError()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = MessageTypes.Ping,
            messageId = MessageId,
            timestampUtc = "2026-08-05T14:00:00+02:00",
            payload = new { }
        });

        var result = _parser.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_envelope", result.Error?.Code);
    }

    private static byte[] CreateEnvelope(string type, object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            messageId = MessageId,
            timestampUtc = TimestampUtc,
            payload
        });
}
