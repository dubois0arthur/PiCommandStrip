namespace PiCommandStrip.App.Spotify;

public sealed class SpotifyStateStore(TimeProvider timeProvider)
{
    private readonly Lock _lock = new();
    private SpotifyState _current = SpotifyState.Unconfigured(timeProvider.GetUtcNow());

    public SpotifyState Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(SpotifyState observation, out SpotifyState changedState)
    {
        lock (_lock)
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
