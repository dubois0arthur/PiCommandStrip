using System.Text.Json;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.Tests.Protocol;

public sealed class BrowserCommandParserTests
{
    [Fact]
    public void Parser_AcceptsFixedBrowserCommandAndConfiguredSearchIdentifier()
    {
        var parser = new ClientMessageParser();

        var reload = parser.Parse(Envelope(new { commandId = PcCommandIds.BrowserReload }));
        var search = parser.Parse(Envelope(new
        {
            commandId = PcCommandIds.BrowserSearchSelection,
            searchActionId = "wikipedia"
        }));

        Assert.Equal(PcCommandIds.BrowserReload, Assert.IsType<CommandRequestMessage>(reload.Message).Payload.CommandId);
        Assert.Equal("wikipedia", Assert.IsType<CommandRequestMessage>(search.Message).Payload.SearchActionId);
    }

    [Theory]
    [InlineData("https://attacker.example/?q=x")]
    [InlineData("../../unsafe")]
    [InlineData("")]
    public void Parser_RejectsArbitrarySearchPayload(string searchActionId)
    {
        var result = new ClientMessageParser().Parse(Envelope(new
        {
            commandId = PcCommandIds.BrowserSearchSelection,
            searchActionId
        }));

        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    [Fact]
    public void Parser_RejectsExtraBrowserCommandData()
    {
        var result = new ClientMessageParser().Parse(Envelope(new
        {
            commandId = PcCommandIds.BrowserReload,
            url = "https://attacker.example"
        }));

        Assert.Equal("invalid_payload", result.Error?.Code);
    }

    private static byte[] Envelope(object payload) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        type = MessageTypes.CommandRequest,
        messageId = Guid.NewGuid(),
        timestampUtc = DateTimeOffset.UtcNow,
        payload
    });
}
