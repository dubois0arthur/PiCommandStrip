namespace PiCommandStrip.App.PcCommands;

public sealed record PcCommandInvocation(
    string CommandId,
    long? PositionMilliseconds = null,
    string? ApplicationId = null,
    float? Volume = null,
    bool? IsMuted = null);
