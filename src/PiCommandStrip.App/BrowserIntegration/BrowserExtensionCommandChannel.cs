using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using PiCommandStrip.App.Protocol;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed record BrowserExtensionCommand(
    string CommandId,
    int? ExpectedActiveTabId,
    string? SearchUrl = null);

public sealed record BrowserExtensionCommandResult(
    Guid RequestMessageId,
    bool Succeeded,
    string Code);

public interface IBrowserExtensionCommandChannel
{
    Task<BrowserExtensionCommandResult> ExecuteAsync(
        BrowserExtensionCommand command,
        CancellationToken cancellationToken);

    Task SendEnvelopeAsync<TPayload>(
        string type,
        TPayload payload,
        CancellationToken cancellationToken);

    bool TryComplete(BrowserExtensionCommandResult result);

    void FailPending();
}

public sealed class BrowserExtensionCommandChannel(
    WebSocket socket,
    TimeProvider timeProvider) : IBrowserExtensionCommandChannel, IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BrowserExtensionCommandResult>> _pending = [];
    private readonly TaskCompletionSource<bool> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task<BrowserExtensionCommandResult> ExecuteAsync(
        BrowserExtensionCommand command,
        CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        try
        {
            await _ready.Task.WaitAsync(CommandTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(messageId, false, "command_timeout");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(messageId, false, "bridge_disconnected");
        }

        var completion = new TaskCompletionSource<BrowserExtensionCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(messageId, completion))
        {
            return new(messageId, false, "command_unavailable");
        }

        try
        {
            await SendEnvelopeCoreAsync(
                BrowserIntegrationProtocol.CommandType,
                messageId,
                new
                {
                    commandId = command.CommandId,
                    expectedActiveTabId = command.ExpectedActiveTabId,
                    searchUrl = command.SearchUrl
                },
                cancellationToken);
            return await completion.Task.WaitAsync(CommandTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(messageId, false, "command_timeout");
        }
        finally
        {
            _pending.TryRemove(messageId, out _);
        }
    }

    public Task SendEnvelopeAsync<TPayload>(
        string type,
        TPayload payload,
        CancellationToken cancellationToken) =>
        SendEnvelopeCoreAsync(type, Guid.NewGuid(), payload, cancellationToken);

    public bool TryComplete(BrowserExtensionCommandResult result) =>
        _pending.TryGetValue(result.RequestMessageId, out var completion) &&
        completion.TrySetResult(result);

    public void FailPending()
    {
        _ready.TrySetCanceled();
        foreach (var (messageId, completion) in _pending)
        {
            completion.TrySetResult(new(messageId, false, "bridge_disconnected"));
        }
    }

    public void MarkReady() => _ready.TrySetResult(true);

    public void Dispose()
    {
        FailPending();
        _sendLock.Dispose();
    }

    private async Task SendEnvelopeCoreAsync<TPayload>(
        string type,
        Guid messageId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            messageId,
            timestampUtc = timeProvider.GetUtcNow(),
            payload
        }, ProtocolJson.SerializerOptions);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new WebSocketException(WebSocketError.InvalidState);
            }

            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
