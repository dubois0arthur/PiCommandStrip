namespace PiCommandStrip.App.PcCommands;

public sealed class PcCommandCooldown
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(2);

    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _duration;
    private DateTimeOffset _nextAllowedAtUtc = DateTimeOffset.MinValue;

    public PcCommandCooldown(TimeProvider timeProvider, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        _timeProvider = timeProvider;
        _duration = duration;
    }

    public bool TryAcquire(out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            if (now < _nextAllowedAtUtc)
            {
                retryAfter = _nextAllowedAtUtc - now;
                return false;
            }

            _nextAllowedAtUtc = now + _duration;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }
}
