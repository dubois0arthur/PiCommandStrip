using System.Net.WebSockets;
using System.Text.Json;
using PiCommandStrip.App.Protocol;
using PiCommandStrip.App.WebSockets;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserIntegrationConnectionHandler(
    BrowserIntegrationMessageParser parser,
    BrowserIntegrationAuthenticationService authenticationService,
    BrowserAuthenticationAttemptLimiter authenticationAttemptLimiter,
    IBrowserIntegrationService integrationService,
    WebSocketMessageReader messageReader,
    IHostApplicationLifetime applicationLifetime,
    ILogger<BrowserIntegrationConnectionHandler> logger)
{
    public async Task HandleAsync(WebSocket socket, CancellationToken requestCancellationToken)
    {
        var connectionId = Guid.NewGuid();
        var authenticated = false;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            applicationLifetime.ApplicationStopping);
        var cancellationToken = linkedCancellation.Token;
        logger.LogInformation("Firefox browser bridge connected on loopback");

        try
        {
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var received = await messageReader.ReadAsync(
                    socket,
                    BrowserIntegrationProtocol.MaximumMessageSizeBytes,
                    cancellationToken);
                if (received.Kind == ReceivedMessageKind.Close)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Browser bridge closed.",
                            cancellationToken);
                    }
                    break;
                }

                if (received.Kind != ReceivedMessageKind.Text)
                {
                    var code = received.Kind == ReceivedMessageKind.TooLarge
                        ? "message_too_large"
                        : "unsupported_data";
                    await SendErrorAsync(socket, null, code, "The browser bridge message was rejected.", cancellationToken);
                    continue;
                }

                var parsed = parser.Parse(received.Content);
                if (!parsed.IsValid)
                {
                    await SendErrorAsync(
                        socket,
                        parsed.Error!.RequestMessageId,
                        parsed.Error.Code,
                        parsed.Error.Message,
                        cancellationToken);
                    continue;
                }

                if (!authenticated)
                {
                    if (parsed.Message is not BrowserHelloMessage hello)
                    {
                        await SendErrorAsync(
                            socket,
                            parsed.Message!.MessageId,
                            "authentication_required",
                            "Authenticate with browser_hello first.",
                            cancellationToken);
                        continue;
                    }

                    if (hello.ProtocolVersion != BrowserIntegrationProtocol.Version)
                    {
                        await SendErrorAsync(
                            socket,
                            hello.MessageId,
                            "unsupported_protocol_version",
                            "The browser bridge protocol version is not supported.",
                            cancellationToken);
                        continue;
                    }

                    if (!authenticationAttemptLimiter.TryBeginAttempt(out _))
                    {
                        logger.LogWarning("Firefox browser bridge authentication was rate limited");
                        await SendErrorAsync(socket, hello.MessageId, "authentication_rate_limited", "Too many pairing attempts.", cancellationToken);
                        await CloseAuthenticationFailureAsync(socket, cancellationToken);
                        break;
                    }

                    var status = authenticationService.Authenticate(
                        hello.AuthenticationToken,
                        hello.TimestampUtc);
                    if (status != BrowserAuthenticationStatus.Authenticated)
                    {
                        logger.LogWarning(
                            "Firefox browser bridge authentication failed with status {AuthenticationStatus}",
                            status);
                        var code = status switch
                        {
                            BrowserAuthenticationStatus.Missing => "authentication_missing",
                            BrowserAuthenticationStatus.Expired => "authentication_expired",
                            _ => "authentication_failed"
                        };
                        await SendErrorAsync(socket, hello.MessageId, code, "Browser bridge authentication failed.", cancellationToken);
                        await CloseAuthenticationFailureAsync(socket, cancellationToken);
                        break;
                    }

                    authenticated = true;
                    authenticationAttemptLimiter.RecordSuccess();
                    await integrationService.BeginConnectionAsync(
                        connectionId,
                        hello.Identity,
                        cancellationToken);
                    await SendAsync(
                        socket,
                        "browser_bridge_ready",
                        new { protocolVersion = BrowserIntegrationProtocol.Version },
                        cancellationToken);
                    logger.LogInformation("Firefox browser bridge authenticated");
                    continue;
                }

                if (parsed.Message is BrowserStateUpdateMessage update)
                {
                    await integrationService.ApplyObservationAsync(
                        connectionId,
                        update.Observation,
                        cancellationToken);
                }
                else
                {
                    await SendErrorAsync(
                        socket,
                        parsed.Message!.MessageId,
                        "already_authenticated",
                        "The browser bridge is already authenticated.",
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Firefox browser bridge connection canceled");
        }
        catch (WebSocketException exception)
        {
            logger.LogInformation(exception, "Firefox browser bridge disconnected unexpectedly");
        }
        finally
        {
            if (authenticated)
            {
                await integrationService.EndConnectionAsync(connectionId, CancellationToken.None);
            }
            socket.Dispose();
            logger.LogInformation("Firefox browser bridge disconnected");
        }
    }

    private static Task SendErrorAsync(
        WebSocket socket,
        Guid? requestMessageId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        SendAsync(socket, "error", new { requestMessageId, code, message }, cancellationToken);

    private static async Task SendAsync<TPayload>(
        WebSocket socket,
        string type,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            messageId = Guid.NewGuid(),
            timestampUtc = DateTimeOffset.UtcNow,
            payload
        }, ProtocolJson.SerializerOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task CloseAuthenticationFailureAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseOutputAsync(
                (WebSocketCloseStatus)4003,
                "Browser bridge authentication failed.",
                cancellationToken);
        }
    }
}
