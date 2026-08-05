namespace PiCommandStrip.App.Protocol;

public sealed class ServerMessageFactory(TimeProvider timeProvider)
{
    public ProtocolEnvelope<TPayload> Create<TPayload>(string type, TPayload payload) =>
        new(type, Guid.NewGuid(), timeProvider.GetUtcNow(), payload);
}
