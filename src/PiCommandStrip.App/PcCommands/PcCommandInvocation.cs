namespace PiCommandStrip.App.PcCommands;

public sealed record PcCommandInvocation(
    string CommandId,
    long? PositionMilliseconds = null,
    string? ApplicationId = null,
    float? Volume = null,
    bool? IsMuted = null,
    string? DeviceId = null,
    bool? IsSaved = null,
    bool? ShuffleEnabled = null,
    string? RepeatState = null,
    string? SearchActionId = null,
    long? ResearchItemId = null);
