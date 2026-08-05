namespace PiCommandStrip.App.ForegroundWindows;

public sealed class ForegroundStateStore
{
    private readonly Lock _sync = new();
    private ForegroundWindowState _current;

    public ForegroundStateStore(TimeProvider timeProvider)
    {
        _current = ForegroundWindowState.Unavailable(timeProvider.GetUtcNow());
    }

    public ForegroundWindowState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(ForegroundWindowState observation, out ForegroundWindowState changedState)
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
