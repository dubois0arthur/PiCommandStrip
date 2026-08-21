namespace PiCommandStrip.App.SystemTelemetry;

public sealed class SystemTelemetryStateStore(TimeProvider timeProvider)
{
    private readonly Lock _sync = new();
    private SystemTelemetryState _current = SystemTelemetryState.Unavailable(timeProvider.GetUtcNow());

    public SystemTelemetryState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(SystemTelemetryState observation, out SystemTelemetryState changedState)
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
