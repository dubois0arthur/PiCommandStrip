using PiCommandStrip.App.Authentication;

namespace PiCommandStrip.App.BrowserIntegration;

public sealed class BrowserAuthenticationAttemptLimiter(TimeProvider timeProvider)
{
    private readonly AuthenticationAttemptLimiter _inner = new(timeProvider);

    public bool TryBeginAttempt(out TimeSpan retryAfter) =>
        _inner.TryBeginAttempt(out retryAfter);

    public void RecordSuccess() => _inner.RecordSuccess();
}
