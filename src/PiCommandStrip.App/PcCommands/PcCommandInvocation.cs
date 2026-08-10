namespace PiCommandStrip.App.PcCommands;

public sealed record PcCommandInvocation(
    string CommandId,
    long? PositionMilliseconds = null);
