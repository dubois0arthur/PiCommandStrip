namespace PiCommandStrip.App.AudioMixer;

public static class AudioSessionStates
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Unknown = "unknown";
}

public static class AudioOutputDeviceStates
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string NotPresent = "not_present";
    public const string Unplugged = "unplugged";
    public const string Unknown = "unknown";
}

public sealed record AudioOutputDeviceDescriptorState(
    string DeviceId,
    string FriendlyName,
    string State,
    bool IsDefault)
{
    public bool HasSameMeaningAs(AudioOutputDeviceDescriptorState other) =>
        string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal) &&
        string.Equals(FriendlyName, other.FriendlyName, StringComparison.Ordinal) &&
        string.Equals(State, other.State, StringComparison.Ordinal) &&
        IsDefault == other.IsDefault;
}

public sealed record AudioOutputDeviceState(
    string DeviceId,
    string FriendlyName,
    float Volume,
    bool IsMuted)
{
    public bool HasSameMeaningAs(AudioOutputDeviceState other) =>
        string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal) &&
        string.Equals(FriendlyName, other.FriendlyName, StringComparison.Ordinal) &&
        Volume.Equals(other.Volume) &&
        IsMuted == other.IsMuted;
}

public sealed record ApplicationAudioState(
    string ApplicationId,
    IReadOnlyList<int> ProcessIds,
    string? ProcessName,
    string DisplayName,
    float Volume,
    bool IsMuted,
    string State,
    int SessionCount,
    bool HasMixedVolume,
    bool HasMixedMute,
    IReadOnlyList<string> SessionInstanceIds)
{
    public bool HasSameMeaningAs(ApplicationAudioState other) =>
        string.Equals(ApplicationId, other.ApplicationId, StringComparison.Ordinal) &&
        ProcessIds.SequenceEqual(other.ProcessIds) &&
        string.Equals(ProcessName, other.ProcessName, StringComparison.Ordinal) &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        Volume.Equals(other.Volume) &&
        IsMuted == other.IsMuted &&
        string.Equals(State, other.State, StringComparison.Ordinal) &&
        SessionCount == other.SessionCount &&
        HasMixedVolume == other.HasMixedVolume &&
        HasMixedMute == other.HasMixedMute &&
        SessionInstanceIds.SequenceEqual(other.SessionInstanceIds);
}

public sealed record AudioState(
    bool IsAvailable,
    AudioOutputDeviceState? OutputDevice,
    IReadOnlyList<AudioOutputDeviceDescriptorState> OutputDevices,
    IReadOnlyList<ApplicationAudioState> Applications,
    long Revision,
    DateTimeOffset LastUpdatedUtc)
{
    public static AudioState Unavailable(DateTimeOffset lastUpdatedUtc) =>
        new(false, null, [], [], 0, lastUpdatedUtc);

    public bool HasSameMeaningAs(AudioState other)
    {
        if (IsAvailable != other.IsAvailable ||
            !SameOutputDevice(OutputDevice, other.OutputDevice) ||
            OutputDevices.Count != other.OutputDevices.Count ||
            Applications.Count != other.Applications.Count)
        {
            return false;
        }

        for (var index = 0; index < OutputDevices.Count; index++)
        {
            if (!OutputDevices[index].HasSameMeaningAs(other.OutputDevices[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < Applications.Count; index++)
        {
            if (!Applications[index].HasSameMeaningAs(other.Applications[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameOutputDevice(
        AudioOutputDeviceState? left,
        AudioOutputDeviceState? right) =>
        left is null
            ? right is null
            : right is not null && left.HasSameMeaningAs(right);
}
