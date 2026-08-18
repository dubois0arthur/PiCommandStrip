using System.Buffers;
using System.Net.WebSockets;

namespace PiCommandStrip.App.WebSockets;

public enum ReceivedMessageKind
{
    Text,
    Close,
    UnsupportedData,
    TooLarge
}

public sealed record ReceivedWebSocketMessage(ReceivedMessageKind Kind, ReadOnlyMemory<byte> Content)
{
    public static ReceivedWebSocketMessage WithoutContent(ReceivedMessageKind kind) =>
        new(kind, ReadOnlyMemory<byte>.Empty);
}

public sealed class WebSocketMessageReader
{
    private const int ReceiveBufferSizeBytes = 4 * 1024;

    public async Task<ReceivedWebSocketMessage> ReadAsync(
        WebSocket socket,
        CancellationToken cancellationToken) =>
        await ReadAsync(
            socket,
            Protocol.ProtocolConstants.MaximumMessageSizeBytes,
            cancellationToken);

    public async Task<ReceivedWebSocketMessage> ReadAsync(
        WebSocket socket,
        int maximumMessageSizeBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSizeBytes];
        var message = new ArrayBufferWriter<byte>();
        var tooLarge = false;
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);

            if (result.MessageType is WebSocketMessageType.Close)
            {
                return ReceivedWebSocketMessage.WithoutContent(ReceivedMessageKind.Close);
            }

            messageType ??= result.MessageType;

            if (!tooLarge && message.WrittenCount + result.Count <= maximumMessageSizeBytes)
            {
                message.Write(buffer.AsSpan(0, result.Count));
            }
            else
            {
                tooLarge = true;
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (tooLarge)
        {
            return ReceivedWebSocketMessage.WithoutContent(ReceivedMessageKind.TooLarge);
        }

        if (messageType is not WebSocketMessageType.Text)
        {
            return ReceivedWebSocketMessage.WithoutContent(ReceivedMessageKind.UnsupportedData);
        }

        return new ReceivedWebSocketMessage(ReceivedMessageKind.Text, message.WrittenMemory.ToArray());
    }
}
