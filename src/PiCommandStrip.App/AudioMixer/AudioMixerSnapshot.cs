namespace PiCommandStrip.App.AudioMixer;

public enum AudioSessionStatus
{
    Unknown,
    Inactive,
    Active,
    Expired
}

public enum AudioOutputDeviceStatus
{
    Unknown,
    Active,
    Disabled,
    NotPresent,
    Unplugged
}

public sealed record AudioOutputDeviceDescriptorSnapshot(
    string? DeviceId,
    string? FriendlyName,
    AudioOutputDeviceStatus State,
    bool IsDefault);

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
    IReadOnlyList<AudioSessionSnapshot> Sessions,
    IReadOnlyList<AudioOutputDeviceDescriptorSnapshot>? OutputDevices = null);
