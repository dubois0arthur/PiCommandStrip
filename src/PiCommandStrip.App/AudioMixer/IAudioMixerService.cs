namespace PiCommandStrip.App.AudioMixer;

public interface IAudioMixerService
{
    AudioState Current { get; }
}

public interface IAudioStateBroadcaster
{
    Task BroadcastAsync(AudioState state, CancellationToken cancellationToken);
}
