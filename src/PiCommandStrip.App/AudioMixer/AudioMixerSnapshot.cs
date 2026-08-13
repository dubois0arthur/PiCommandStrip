namespace PiCommandStrip.App.AudioMixer;

public enum AudioSessionStatus
{
    Unknown,
    Inactive,
    Active,
    Expired
}

public sealed record AudioOutputDeviceSnapshot(
    string? DeviceId,
    string? FriendlyName,
    float Volume,
    bool IsMuted);

public sealed record AudioSessionSnapshot(
    string? SessionInstanceId,
    string? SessionIdentifier,
    Guid? GroupingId,
    int? ProcessId,
    string? ProcessName,
    string? DisplayName,
    float Volume,
    bool IsMuted,
    AudioSessionStatus State,
    bool IsSystemSoundsSession = false);

public sealed record AudioMixerSnapshot(
    AudioOutputDeviceSnapshot? OutputDevice,
    IReadOnlyList<AudioSessionSnapshot> Sessions);
