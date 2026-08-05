using PiCommandStrip.App.PcCommands;

namespace PiCommandStrip.Tests.PcCommands;

public sealed class PcCommandCooldownTests
{
    [Fact]
    public void TryAcquire_BlocksRepeatedAttemptsUntilCooldownExpires()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var cooldown = new PcCommandCooldown(clock, TimeSpan.FromSeconds(2));

        Assert.True(cooldown.TryAcquire(out var firstRetryAfter));
        Assert.Equal(TimeSpan.Zero, firstRetryAfter);

        Assert.False(cooldown.TryAcquire(out var immediateRetryAfter));
        Assert.Equal(TimeSpan.FromSeconds(2), immediateRetryAfter);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cooldown.TryAcquire(out var laterRetryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), laterRetryAfter);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(cooldown.TryAcquire(out var expiredRetryAfter));
        Assert.Equal(TimeSpan.Zero, expiredRetryAfter);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
