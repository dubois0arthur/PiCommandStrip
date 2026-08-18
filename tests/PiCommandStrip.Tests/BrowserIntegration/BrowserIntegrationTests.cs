using System.Text.Json;
using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.Tests.BrowserIntegration;

public sealed class BrowserIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ValidToken = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    [Fact]
    public void Configuration_Disabled_DoesNotRequireToken()
    {
        var configuration = BrowserIntegrationConfiguration.Create(
            new BrowserIntegrationOptions(),
            5077);

        Assert.False(configuration.Enabled);
        Assert.Equal(5078, configuration.Port);
    }

    [Fact]
    public void Configuration_Enabled_RequiresSeparateValidTokenAndPort()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BrowserIntegrationConfiguration.Create(
                new BrowserIntegrationOptions { Enabled = true, Port = 5078 },
                5077));
        Assert.Throws<InvalidOperationException>(() =>
            BrowserIntegrationConfiguration.Create(
                new BrowserIntegrationOptions
                {
                    Enabled = true,
                    Port = 5077,
                    Token = ValidToken
                },
                5077));
    }

    [Fact]
    public void Authentication_AcceptsOnlyConfiguredFreshToken()
    {
        var time = new TestTimeProvider(Now);
        var configuration = BrowserIntegrationConfiguration.Create(
            new BrowserIntegrationOptions
            {
                Enabled = true,
                Port = 5078,
                Token = ValidToken
            },
            5077);
        var service = new BrowserIntegrationAuthenticationService(configuration, time);

        Assert.Equal(
            BrowserAuthenticationStatus.Authenticated,
            service.Authenticate(ValidToken, Now));
        Assert.Equal(
            BrowserAuthenticationStatus.Incorrect,
            service.Authenticate(Convert.ToBase64String(new byte[32]), Now));
        Assert.Equal(
            BrowserAuthenticationStatus.Expired,
            service.Authenticate(ValidToken, Now - TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Parser_AcceptsTypedSnapshot_AndRejectsMalformedOrExtraData()
    {
        var parser = new BrowserIntegrationMessageParser();
        var valid = StateEnvelope(new
        {
            activeTabId = 42,
            url = "https://example.com/research",
            title = "Research",
            selectedText = "selection",
            canGoBack = (bool?)null,
            canGoForward = (bool?)null
        });

        var result = parser.Parse(valid);

        var update = Assert.IsType<BrowserStateUpdateMessage>(result.Message);
        Assert.Equal(42, update.Observation.ActiveTabId);
        Assert.Equal("selection", update.Observation.SelectedText);
        Assert.Equal("malformed_json", parser.Parse("{"u8.ToArray()).Error?.Code);
        Assert.Equal(
            "invalid_payload",
            parser.Parse(StateEnvelope(new
            {
                activeTabId = 42,
                url = "https://example.com",
                title = "Title",
                selectedText = (string?)null,
                canGoBack = (bool?)null,
                canGoForward = (bool?)null,
                arbitrary = "not allowed"
            })).Error?.Code);
    }

    [Fact]
    public void Normalizer_SanitizesUrlDomainAndSafelyCapsSelectedText()
    {
        var normalizer = new BrowserStateNormalizer();
        var connected = normalizer.Connected(
            new BrowserIdentity("Firefox", "extension-id", "instance-id"),
            Now);
        var oversized = new string('x', BrowserStateNormalizer.MaximumSelectedTextLength - 1) +
            "😀tail";

        var state = normalizer.Normalize(
            connected,
            new BrowserTabObservation(
                7,
                "https://user:password@Exämple.com/path?q=1#private-fragment",
                " Page title ",
                oversized,
                null,
                null),
            Now);

        Assert.Equal("xn--exmple-cua.com", state.HostName);
        Assert.DoesNotContain("user", state.Url);
        Assert.DoesNotContain("fragment", state.Url);
        Assert.Equal(BrowserStateNormalizer.MaximumSelectedTextLength - 1, state.SelectedText?.Length);
        Assert.Equal("Page title", state.PageTitle);
    }

    [Fact]
    public async Task Service_Deduplicates_ClearsSelection_AndIgnoresStaleDisconnect()
    {
        var time = new TestTimeProvider(Now);
        var broadcaster = new RecordingBroadcaster();
        var service = new BrowserIntegrationService(
            new BrowserStateStore(time),
            new BrowserStateNormalizer(),
            broadcaster,
            time);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var identity = new BrowserIdentity("firefox", "extension", "instance");
        var selected = new BrowserTabObservation(
            1,
            "https://example.com/one",
            "One",
            "selected",
            null,
            null);

        await service.BeginConnectionAsync(first, identity, CancellationToken.None);
        await service.SetBrowserContextActiveAsync(true, CancellationToken.None);
        await service.ApplyObservationAsync(first, selected, CancellationToken.None);
        await service.ApplyObservationAsync(first, selected, CancellationToken.None);
        Assert.Equal(2, broadcaster.States.Count);

        await service.ApplyObservationAsync(
            first,
            selected with
            {
                ActiveTabId = 2,
                Url = "https://example.org/two",
                PageTitle = "Two",
                SelectedText = null
            },
            CancellationToken.None);
        Assert.Null(service.Current.SelectedText);
        Assert.Equal(2, service.Current.ActiveTabId);

        await service.ApplyObservationAsync(
            first,
            selected with
            {
                ActiveTabId = 2,
                Url = "https://example.org/two",
                PageTitle = "Two"
            },
            CancellationToken.None);
        Assert.Equal("selected", service.Current.SelectedText);
        await service.SetBrowserContextActiveAsync(false, CancellationToken.None);
        Assert.Null(service.Current.SelectedText);

        await service.BeginConnectionAsync(second, identity with { InstanceIdentifier = "new" }, CancellationToken.None);
        await service.EndConnectionAsync(first, CancellationToken.None);
        Assert.True(service.Current.IsConnected);

        await service.EndConnectionAsync(second, CancellationToken.None);
        Assert.False(service.Current.IsConnected);
        Assert.Null(service.Current.Url);
        Assert.Null(service.Current.SelectedText);
    }

    private static byte[] StateEnvelope(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = BrowserIntegrationProtocol.StateUpdateType,
            messageId = Guid.NewGuid(),
            timestampUtc = Now,
            payload
        });

    private sealed class RecordingBroadcaster : IBrowserStateBroadcaster
    {
        public List<BrowserState> States { get; } = [];

        public Task BroadcastAsync(BrowserState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
