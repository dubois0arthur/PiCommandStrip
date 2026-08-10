namespace PiCommandStrip.App.MediaSessions;

public sealed class MediaStateStore(TimeProvider timeProvider)
{
    private readonly Lock _sync = new();
    private MediaState _current = MediaState.Inactive(timeProvider.GetUtcNow());

    public MediaState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(MediaState observation, out MediaState changedState)
    {
        lock (_sync)
        {
            if (_current.HasSameMeaningAs(observation))
            {
                changedState = _current;
                return false;
            }

            _current = observation;
            changedState = observation;
            return true;
        }
    }
}
