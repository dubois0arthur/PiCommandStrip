using System.Text.Json;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.Tests.Protocol;

public sealed class AudioCommandParserTests
{
    private static readonly string ApplicationId = new('b', 64);
    private static readonly Guid MessageId =
        Guid.Parse("e11cd28c-4149-4d8d-899f-a3b5c1ce1971");
    private static readonly DateTimeOffset TimestampUtc =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly ClientMessageParser _parser = new();

    [Fact]
    public void Parse_MasterVolume_ReturnsTypedNormalizedValue()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetMasterVolume,
            volume = 0.375
        }));

        var message = Assert.IsType<CommandRequestMessage>(result.Message);
        Assert.Equal(0.375f, message.Payload.Volume);
        Assert.Null(message.Payload.ApplicationId);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void Parse_VolumeOutsideNormalizedRange_IsRejected(double volume)
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetMasterVolume,
            volume
        }));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    [Fact]
    public void Parse_ApplicationMute_ReturnsTypedIdentifierAndState()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetApplicationMute,
            applicationId = ApplicationId,
            isMuted = true
        }));

        var message = Assert.IsType<CommandRequestMessage>(result.Message);
        Assert.Equal(ApplicationId, message.Payload.ApplicationId);
        Assert.True(message.Payload.IsMuted);
    }

    [Fact]
    public void Parse_AudioCommandWithUnexpectedProperty_IsRejected()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetApplicationVolume,
            applicationId = ApplicationId,
            volume = 0.5,
            executablePath = "not-accepted.exe"
        }));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    [Fact]
    public void Parse_OutputDevice_ReturnsOpaqueBoundedIdentifier()
    {
        const string deviceId = "{0.0.0.00000000}.{fixture-endpoint}";

        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetOutputDevice,
            deviceId
        }));

        var message = Assert.IsType<CommandRequestMessage>(result.Message);
        Assert.Equal(deviceId, message.Payload.DeviceId);
        Assert.Null(message.Payload.ApplicationId);
    }

    [Fact]
    public void Parse_OutputDeviceWithExecutablePathProperty_IsRejected()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.AudioSetOutputDevice,
            deviceId = "known-id",
            executablePath = "not-accepted.exe"
        }));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    private static byte[] Envelope(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = MessageTypes.CommandRequest,
            messageId = MessageId,
            timestampUtc = TimestampUtc,
            payload
        });
}
