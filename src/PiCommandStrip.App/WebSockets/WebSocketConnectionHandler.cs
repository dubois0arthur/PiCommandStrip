using System.Net.WebSockets;
using System.Text.Json;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.WebSockets;

public sealed class WebSocketConnectionHandler(
    ClientMessageParser parser,
    ServerMessageFactory messageFactory,
    WebSocketMessageReader messageReader,
    IHostApplicationLifetime applicationLifetime,
    ILogger<WebSocketConnectionHandler> logger)
{
    public async Task HandleAsync(WebSocket socket, CancellationToken requestCancellationToken)
    {
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            applicationLifetime.ApplicationStopping);
        var cancellationToken = connectionCancellation.Token;

        logger.LogInformation("WebSocket client connected");

        try
        {
            await SendAsync(
                socket,
                messageFactory.Create(
                    MessageTypes.ServerHello,
                    new ServerHelloPayload(
                        "PiCommandStrip.App",
                        ProtocolConstants.Version,
                        ProtocolConstants.MaximumMessageSizeBytes)),
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
                        socket,
                        null,
                        "message_too_large",
                        $"Messages must not exceed {ProtocolConstants.MaximumMessageSizeBytes} bytes.",
                        cancellationToken);
                    continue;
                }

                if (received.Kind is ReceivedMessageKind.UnsupportedData)
                {
                    await SendErrorAsync(
                        socket,
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
                        socket,
                        error.RequestMessageId,
                        error.Code,
                        error.Message,
                        cancellationToken);
                    continue;
                }

                await DispatchAsync(socket, parseResult.Message!, cancellationToken);
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
        WebSocket socket,
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ClientHelloMessage clientHello:
                if (clientHello.Payload.ProtocolVersion != ProtocolConstants.Version)
                {
                    await SendErrorAsync(
                        socket,
                        clientHello.MessageId,
                        "unsupported_protocol_version",
                        $"Protocol version '{clientHello.Payload.ProtocolVersion}' is not supported.",
                        cancellationToken);
                    return;
                }

                logger.LogInformation("WebSocket client identified as {ClientName}", clientHello.Payload.ClientName);
                return;

            case PingMessage ping:
                await SendAsync(
                    socket,
                    messageFactory.Create(MessageTypes.Pong, new PongPayload(ping.MessageId)),
                    cancellationToken);
                return;

            case CommandRequestMessage commandRequest:
                logger.LogInformation(
                    "Command {CommandId} requested but command execution is not implemented",
                    commandRequest.Payload.CommandId);
                await SendAsync(
                    socket,
                    messageFactory.Create(
                        MessageTypes.CommandResult,
                        new CommandResultPayload(
                            commandRequest.MessageId,
                            commandRequest.Payload.CommandId,
                            false,
                            "PC commands are not available in this protocol-only milestone.")),
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"Unhandled parsed message type {message.GetType().Name}.");
        }
    }

    private Task SendErrorAsync(
        WebSocket socket,
        Guid? requestMessageId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        SendAsync(
            socket,
            messageFactory.Create(
                MessageTypes.Error,
                new ErrorPayload(requestMessageId, code, message)),
            cancellationToken);

    private static async Task SendAsync<TPayload>(
        WebSocket socket,
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.SerializerOptions);
        await socket.SendAsync(json, WebSocketMessageType.Text, true, cancellationToken);
    }

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
