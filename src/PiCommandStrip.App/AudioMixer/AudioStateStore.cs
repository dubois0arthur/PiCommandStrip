namespace PiCommandStrip.App.AudioMixer;

public sealed class AudioStateStore(TimeProvider timeProvider)
{
    private readonly Lock _sync = new();
    private AudioState _current = AudioState.Unavailable(timeProvider.GetUtcNow());

    public AudioState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(AudioState observation, out AudioState changedState)
    {
        lock (_sync)
        {
            if (_current.HasSameMeaningAs(observation))
            {
                changedState = _current;
                return false;
            }

            _current = observation with { Revision = _current.Revision + 1 };
            changedState = _current;
            return true;
        }
    }
}
