using System.Net.WebSockets;
using PiCommandStrip.App.Authentication;
using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketConnectionHandler(
    ClientMessageParser parser,
    ServerMessageFactory messageFactory,
    WebSocketMessageReader messageReader,
    ForegroundStateStore foregroundStateStore,
    ContextStateCoordinator contextStateCoordinator,
    ContextCatalog contextCatalog,
    IMediaSessionService mediaSessionService,
    IAudioMixerService audioMixerService,
    IPcCommandDispatcher commandDispatcher,
    ClientAuthenticationService authenticationService,
    AuthenticationAttemptLimiter authenticationAttemptLimiter,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<WebSocketConnectionHandler> logger)
{
    public async Task HandleAsync(
        WebSocketClientConnection connection,
        CancellationToken requestCancellationToken)
    {
        var socket = connection.Socket;
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            applicationLifetime.ApplicationStopping);
        var cancellationToken = connectionCancellation.Token;

        logger.LogInformation("WebSocket client connected");

        try
        {
            await connection.SendAsync(
                messageFactory.Create(
                    MessageTypes.ServerHello,
                    new ServerHelloPayload(
                        "PiCommandStrip.App",
                        ProtocolConstants.Version,
                        ProtocolConstants.MaximumMessageSizeBytes,
                        contextCatalog.Profiles
                            .Select(profile => new ContextDescriptorPayload(
                                profile.Id,
                                profile.DisplayName))
                            .ToArray())),
                cancellationToken);

            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var received = await messageReader.ReadAsync(socket, cancellationToken);

                if (received.Kind is ReceivedMessageKind.Close)
                {
                    await CompleteClientCloseAsync(socket, cancellationToken);
                    break;
                }

                if (received.Kind is ReceivedMessageKind.TooLarge)
                {
                    await SendErrorAsync(
                        connection,
                        null,
                        "message_too_large",
                        $"Messages must not exceed {ProtocolConstants.MaximumMessageSizeBytes} bytes.",
                        cancellationToken);
                    continue;
                }

                if (received.Kind is ReceivedMessageKind.UnsupportedData)
                {
                    await SendErrorAsync(
                        connection,
                        null,
                        "unsupported_data",
                        "Only UTF-8 JSON text messages are supported.",
                        cancellationToken);
                    continue;
                }

                var parseResult = parser.Parse(received.Content);
                if (!parseResult.IsValid)
                {
                    var error = parseResult.Error!;
                    await SendErrorAsync(
                        connection,
                        error.RequestMessageId,
                        error.Code,
                        error.Message,
                        cancellationToken);
                    continue;
                }

                await DispatchAsync(connection, parseResult.Message!, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("WebSocket connection canceled");
        }
        catch (WebSocketException exception)
        {
            logger.LogInformation(exception, "WebSocket client disconnected unexpectedly");
        }
        finally
        {
            await CloseForShutdownIfNeededAsync(socket);
            logger.LogInformation("WebSocket client disconnected");
        }
    }

    private async Task DispatchAsync(
        WebSocketClientConnection connection,
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ClientHelloMessage clientHello:
                if (clientHello.Payload.ProtocolVersion != ProtocolConstants.Version)
                {
                    await SendErrorAsync(
                        connection,
                        clientHello.MessageId,
                        "unsupported_protocol_version",
                        $"Protocol version '{clientHello.Payload.ProtocolVersion}' is not supported.",
                        cancellationToken);
                    return;
                }

                if (connection.IsAuthenticated)
                {
                    await SendErrorAsync(
                        connection,
                        clientHello.MessageId,
                        "already_authenticated",
                        "This connection is already authenticated.",
                        cancellationToken);
                    return;
                }

                if (!authenticationAttemptLimiter.TryBeginAttempt(out var retryAfter))
                {
                    logger.LogWarning(
                        "WebSocket authentication attempt rate limited for connection {ConnectionId}",
                        connection.ConnectionId);
                    await SendErrorAsync(
                        connection,
                        clientHello.MessageId,
                        "authentication_rate_limited",
                        $"Too many authentication attempts. Try again in {Math.Ceiling(retryAfter.TotalSeconds)} seconds.",
                        cancellationToken);
                    return;
                }

                var authenticationStatus = authenticationService.Authenticate(
                    clientHello.Payload.AuthenticationToken,
                    clientHello.TimestampUtc);
                if (authenticationStatus is not ClientAuthenticationStatus.Authenticated)
                {
                    logger.LogWarning(
                        "WebSocket authentication failed with status {AuthenticationStatus} for connection {ConnectionId}",
                        authenticationStatus,
                        connection.ConnectionId);
                    var (code, errorMessage) = authenticationStatus switch
                    {
                        ClientAuthenticationStatus.Missing =>
                            ("authentication_missing", "An authentication token is required."),
                        ClientAuthenticationStatus.Expired =>
                            ("authentication_expired", "The authentication attempt has expired. Reconnect and try again."),
                        _ =>
                            ("authentication_failed", "Authentication failed.")
                    };
                    await SendErrorAsync(connection, clientHello.MessageId, code, errorMessage, cancellationToken);
                    return;
                }

                connection.MarkAuthenticated();
                authenticationAttemptLimiter.RecordSuccess();

                logger.LogInformation(
                    "WebSocket client {ClientName} authenticated for connection {ConnectionId}",
                    clientHello.Payload.ClientName,
                    connection.ConnectionId);
                await connection.InitializeStateAsync(
                    foregroundStateStore,
                    contextStateCoordinator,
                    mediaSessionService,
                    audioMixerService,
                    messageFactory,
                    cancellationToken);
                return;

            case PingMessage ping:
                if (!await EnsureAuthenticatedAsync(connection, ping.MessageId, cancellationToken))
                {
                    return;
                }

                await connection.SendAsync(
                    messageFactory.Create(MessageTypes.Pong, new PongPayload(ping.MessageId)),
                    cancellationToken);
                return;

            case ContextSelectionRequestMessage contextSelectionRequest:
                if (!await EnsureAuthenticatedAsync(
                    connection,
                    contextSelectionRequest.MessageId,
                    cancellationToken))
                {
                    return;
                }

                await HandleContextSelectionRequestAsync(
                    connection,
                    contextSelectionRequest,
                    cancellationToken);
                return;

            case CommandRequestMessage commandRequest:
                if (!await EnsureAuthenticatedAsync(connection, commandRequest.MessageId, cancellationToken))
                {
                    return;
                }

                await HandleCommandRequestAsync(connection, commandRequest, cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"Unhandled parsed message type {message.GetType().Name}.");
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(
        WebSocketClientConnection connection,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        if (connection.IsAuthenticated)
        {
            return true;
        }

        logger.LogWarning(
            "Rejected unauthenticated WebSocket message for connection {ConnectionId}",
            connection.ConnectionId);
        await SendErrorAsync(
            connection,
            requestMessageId,
            "authentication_required",
            "Authenticate with client_hello before sending this message.",
            cancellationToken);
        return false;
    }

    private async Task HandleCommandRequestAsync(
        WebSocketClientConnection connection,
        CommandRequestMessage commandRequest,
        CancellationToken cancellationToken)
    {
        PcCommandExecutionResult result;

        if (!connection.CommandCooldown.TryAcquire(out _))
        {
            logger.LogInformation(
                "PC command request rate limited for connection {ConnectionId}",
                connection.ConnectionId);
            result = PcCommandExecutionResult.Failure("Please wait before sending another command.");
        }
        else
        {
            result = await commandDispatcher.DispatchAsync(
                new PcCommandInvocation(
                    commandRequest.Payload.CommandId,
                    commandRequest.Payload.PositionMilliseconds),
                cancellationToken);
        }

        await connection.SendAsync(
            messageFactory.Create(
                MessageTypes.CommandResult,
                new CommandResultPayload(
                    commandRequest.MessageId,
                    commandRequest.Payload.CommandId,
                    result.Succeeded,
                    result.Message,
                    timeProvider.GetUtcNow())),
            cancellationToken);
    }

    private async Task HandleContextSelectionRequestAsync(
        WebSocketClientConnection connection,
        ContextSelectionRequestMessage request,
        CancellationToken cancellationToken)
    {
        var result = request.Payload.Mode == ContextSelectionModes.Automatic
            ? await contextStateCoordinator.UseAutomaticAsync(cancellationToken)
            : await contextStateCoordinator.PinAsync(request.Payload.ContextId!, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Rejected unavailable context identifier for connection {ConnectionId}",
                connection.ConnectionId);
        }
        else
        {
            logger.LogInformation(
                "Context selection changed to {SelectionMode} context {ContextId} for connection {ConnectionId}",
                result.State.SelectionMode,
                result.State.ContextId,
                connection.ConnectionId);
        }

        await connection.SendAsync(
            messageFactory.Create(
                MessageTypes.ContextSelectionResult,
                new ContextSelectionResultPayload(
                    request.MessageId,
                    result.Succeeded,
                    result.Message,
                    timeProvider.GetUtcNow())),
            cancellationToken);
    }

    private Task SendErrorAsync(
        WebSocketClientConnection connection,
        Guid? requestMessageId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        connection.SendAsync(
            messageFactory.Create(
                MessageTypes.Error,
                new ErrorPayload(requestMessageId, code, message)),
            cancellationToken);

    private static async Task CompleteClientCloseAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.CloseReceived)
        {
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Connection closed.",
                cancellationToken);
        }
    }

    private static async Task CloseForShutdownIfNeededAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.EndpointUnavailable,
                "Server connection ending.",
                closeTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            socket.Abort();
        }
        catch (WebSocketException)
        {
            socket.Abort();
        }
    }
}
