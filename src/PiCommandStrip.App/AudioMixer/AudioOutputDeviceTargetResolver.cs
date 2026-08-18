namespace PiCommandStrip.App.AudioMixer;

public static class AudioOutputDeviceTargetResolver
{
    public const int MaximumDeviceIdLength = 512;

    public static bool IsValidDeviceIdShape(string? deviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) &&
        deviceId.Length <= MaximumDeviceIdLength;

    public static AudioOutputDeviceDescriptorState? ResolveActiveDevice(
        AudioState state,
        string deviceId)
    {
        if (!IsValidDeviceIdShape(deviceId))
        {
            return null;
        }

        return state.OutputDevices.FirstOrDefault(device =>
            device.State == AudioOutputDeviceStates.Active &&
            string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
    }
}
