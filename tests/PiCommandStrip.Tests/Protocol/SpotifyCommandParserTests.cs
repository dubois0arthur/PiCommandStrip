using System.Text.Json;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;
using PiCommandStrip.App.Spotify;

namespace PiCommandStrip.Tests.Protocol;

public sealed class SpotifyCommandParserTests
{
    private readonly ClientMessageParser _parser = new();

    [Fact]
    public void Parse_SetSaved_ReturnsTypedBoolean()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.SpotifySetSaved,
            isSaved = true
        }));

        var message = Assert.IsType<CommandRequestMessage>(result.Message);
        Assert.True(message.Payload.IsSaved);
    }

    [Theory]
    [InlineData(SpotifyRepeatStates.Off)]
    [InlineData(SpotifyRepeatStates.Context)]
    [InlineData(SpotifyRepeatStates.Track)]
    public void Parse_SetRepeat_AcceptsOnlyKnownState(string repeatState)
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.SpotifySetRepeat,
            repeatState
        }));

        Assert.True(result.IsValid);
        Assert.Equal(repeatState, Assert.IsType<CommandRequestMessage>(result.Message).Payload.RepeatState);
    }

    [Fact]
    public void Parse_SpotifyCommandWithUnexpectedSecretProperty_IsRejected()
    {
        var result = _parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.SpotifySetShuffle,
            shuffleEnabled = true,
            accessToken = "must-not-be-accepted"
        }));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    private static byte[] Envelope(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = MessageTypes.CommandRequest,
            messageId = Guid.NewGuid(),
            timestampUtc = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
            payload
        });
}
