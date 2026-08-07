namespace PiCommandStrip.App.Authentication;

public sealed class AuthenticationAttemptLimiter
{
    public const int DefaultMaximumAttempts = 5;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maximumAttempts;
    private readonly TimeSpan _window;
    private DateTimeOffset _windowEndsAtUtc = DateTimeOffset.MinValue;
    private int _attemptCount;

    public AuthenticationAttemptLimiter(
        TimeProvider timeProvider,
        int maximumAttempts = DefaultMaximumAttempts,
        TimeSpan? window = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        _timeProvider = timeProvider;
        _maximumAttempts = maximumAttempts;
        _window = window ?? DefaultWindow;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_window, TimeSpan.Zero);
    }

    public bool TryBeginAttempt(out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            if (now >= _windowEndsAtUtc)
            {
                _windowEndsAtUtc = now + _window;
                _attemptCount = 0;
            }

            if (_attemptCount >= _maximumAttempts)
            {
                retryAfter = _windowEndsAtUtc - now;
                return false;
            }

            _attemptCount++;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_sync)
        {
            _attemptCount = 0;
            _windowEndsAtUtc = DateTimeOffset.MinValue;
        }
    }
}
