namespace PiCommandStrip.App.AudioMixer;

public interface IAudioMixerService
{
    AudioState Current { get; }

    Task<AudioMixerCommandResult> SetMasterVolumeAsync(
        float volume,
        CancellationToken cancellationToken);

    Task<AudioMixerCommandResult> SetMasterMuteAsync(
        bool isMuted,
        CancellationToken cancellationToken);

    Task<AudioMixerCommandResult> SetApplicationVolumeAsync(
        string applicationId,
        float volume,
        CancellationToken cancellationToken);

    Task<AudioMixerCommandResult> SetApplicationMuteAsync(
        string applicationId,
        bool isMuted,
        CancellationToken cancellationToken);

    Task<AudioMixerCommandResult> SetOutputDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken);
}

public interface IAudioStateBroadcaster
{
    Task BroadcastAsync(AudioState state, CancellationToken cancellationToken);
}
