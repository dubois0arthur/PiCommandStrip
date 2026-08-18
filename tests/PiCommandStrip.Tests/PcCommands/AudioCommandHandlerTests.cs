using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.Tests.PcCommands;

public sealed class AudioCommandHandlerTests
{
    private static readonly string ApplicationId = new('a', 64);

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public async Task SetMasterVolume_InvalidValue_IsRejectedWithoutCallingService(float volume)
    {
        var service = new RecordingAudioMixerService();
        var handler = new AudioCommandHandler(PcCommandIds.AudioSetMasterVolume, service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(PcCommandIds.AudioSetMasterVolume, Volume: volume),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, service.CallCount);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.42f)]
    [InlineData(1f)]
    public async Task SetApplicationVolume_ValidValue_DelegatesTypedTarget(
        float volume)
    {
        var service = new RecordingAudioMixerService();
        var handler = new AudioCommandHandler(
            PcCommandIds.AudioSetApplicationVolume,
            service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(
                PcCommandIds.AudioSetApplicationVolume,
                ApplicationId: ApplicationId,
                Volume: volume),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ApplicationId, service.ApplicationId);
        Assert.Equal(volume, service.Volume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetMasterMute_DelegatesTypedState(bool isMuted)
    {
        var service = new RecordingAudioMixerService();
        var handler = new AudioCommandHandler(PcCommandIds.AudioSetMasterMute, service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(PcCommandIds.AudioSetMasterMute, IsMuted: isMuted),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(isMuted, service.IsMuted);
    }

    [Fact]
    public async Task SetApplicationMute_MissingIdentifier_IsRejected()
    {
        var service = new RecordingAudioMixerService();
        var handler = new AudioCommandHandler(
            PcCommandIds.AudioSetApplicationMute,
            service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(
                PcCommandIds.AudioSetApplicationMute,
                IsMuted: true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task SetOutputDevice_ValidIdentifier_DelegatesOpaqueIdentifier()
    {
        const string deviceId = "{0.0.0.00000000}.fixture-output";
        var service = new RecordingAudioMixerService();
        var handler = new AudioCommandHandler(
            PcCommandIds.AudioSetOutputDevice,
            service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(
                PcCommandIds.AudioSetOutputDevice,
                DeviceId: deviceId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(deviceId, service.DeviceId);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task SetOutputDevice_ServiceReportsStaleIdentifier_ReturnsSafeFailure()
    {
        var service = new RecordingAudioMixerService
        {
            OutputDeviceResult = AudioMixerCommandResult.Failure(
                "That audio output device is no longer available.")
        };
        var handler = new AudioCommandHandler(
            PcCommandIds.AudioSetOutputDevice,
            service);

        var result = await handler.ExecuteAsync(
            new PcCommandInvocation(
                PcCommandIds.AudioSetOutputDevice,
                DeviceId: "stale-device"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("COM", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingAudioMixerService : IAudioMixerService
    {
        public AudioState Current { get; } = AudioState.Unavailable(DateTimeOffset.UtcNow);

        public int CallCount { get; private set; }

        public string? ApplicationId { get; private set; }

        public float? Volume { get; private set; }

        public bool? IsMuted { get; private set; }

        public string? DeviceId { get; private set; }

        public AudioMixerCommandResult OutputDeviceResult { get; init; } =
            AudioMixerCommandResult.Success("Output device changed.");

        public Task<AudioMixerCommandResult> SetMasterVolumeAsync(
            float volume,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Volume = volume;
            return Success();
        }

        public Task<AudioMixerCommandResult> SetMasterMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IsMuted = isMuted;
            return Success();
        }

        public Task<AudioMixerCommandResult> SetApplicationVolumeAsync(
            string applicationId,
            float volume,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ApplicationId = applicationId;
            Volume = volume;
            return Success();
        }

        public Task<AudioMixerCommandResult> SetApplicationMuteAsync(
            string applicationId,
            bool isMuted,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ApplicationId = applicationId;
            IsMuted = isMuted;
            return Success();
        }

        public Task<AudioMixerCommandResult> SetOutputDeviceAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            DeviceId = deviceId;
            return Task.FromResult(OutputDeviceResult);
        }

        private static Task<AudioMixerCommandResult> Success() =>
            Task.FromResult(AudioMixerCommandResult.Success("Updated."));
    }
}
