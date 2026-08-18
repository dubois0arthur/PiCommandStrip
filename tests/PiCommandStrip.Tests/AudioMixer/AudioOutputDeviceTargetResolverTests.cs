using PiCommandStrip.App.AudioMixer;

namespace PiCommandStrip.Tests.AudioMixer;

public sealed class AudioOutputDeviceTargetResolverTests
{
    [Fact]
    public void ResolveActiveDevice_CurrentEnumeratedIdentifier_ReturnsExactTarget()
    {
        var state = State(Device("speakers", isDefault: true), Device("headphones"));

        var resolved = AudioOutputDeviceTargetResolver.ResolveActiveDevice(
            state,
            "headphones");

        Assert.NotNull(resolved);
        Assert.Equal("headphones", resolved.DeviceId);
        Assert.False(resolved.IsDefault);
    }

    [Fact]
    public void ResolveActiveDevice_UnknownIdentifier_IsRejected()
    {
        var state = State(Device("speakers", isDefault: true));

        var resolved = AudioOutputDeviceTargetResolver.ResolveActiveDevice(
            state,
            "C:\\arbitrary\\not-a-device.exe");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveActiveDevice_DeviceDisappearsBetweenSnapshots_BecomesStale()
    {
        var beforeRemoval = State(
            Device("speakers", isDefault: true),
            Device("usb-dac"));
        var afterRemoval = State(Device("speakers", isDefault: true));

        Assert.NotNull(AudioOutputDeviceTargetResolver.ResolveActiveDevice(
            beforeRemoval,
            "usb-dac"));
        Assert.Null(AudioOutputDeviceTargetResolver.ResolveActiveDevice(
            afterRemoval,
            "usb-dac"));
    }

    [Fact]
    public void ResolveActiveDevice_NonActiveEntry_IsRejected()
    {
        var state = State(new AudioOutputDeviceDescriptorState(
            "unplugged-headset",
            "Headset",
            AudioOutputDeviceStates.Unplugged,
            false));

        Assert.Null(AudioOutputDeviceTargetResolver.ResolveActiveDevice(
            state,
            "unplugged-headset"));
    }

    private static AudioOutputDeviceDescriptorState Device(
        string id,
        bool isDefault = false) =>
        new(id, id, AudioOutputDeviceStates.Active, isDefault);

    private static AudioState State(
        params AudioOutputDeviceDescriptorState[] devices) =>
        new(
            true,
            new AudioOutputDeviceState("speakers", "Speakers", 0.5f, false),
            devices,
            [],
            1,
            DateTimeOffset.UtcNow);
}
