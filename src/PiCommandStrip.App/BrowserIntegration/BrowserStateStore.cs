namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserStateStore
{
    private readonly Lock _sync = new();
    private BrowserState _current;

    public BrowserStateStore(TimeProvider timeProvider) =>
        _current = BrowserState.Disconnected(timeProvider.GetUtcNow());

    public BrowserState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryUpdate(BrowserState state)
    {
        lock (_sync)
        {
            if (_current.HasSameMeaningAs(state))
            {
                return false;
            }

            _current = state;
            return true;
        }
    }
}
