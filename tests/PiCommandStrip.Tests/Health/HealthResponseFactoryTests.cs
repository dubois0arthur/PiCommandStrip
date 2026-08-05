using PiCommandStrip.App.Health;

namespace PiCommandStrip.Tests.Health;

public sealed class HealthResponseFactoryTests
{
    [Fact]
    public void Create_ReturnsExpectedIdentityStatusAndCurrentUtcTime()
    {
        var expectedTime = new DateTimeOffset(2026, 8, 5, 10, 30, 0, TimeSpan.Zero);
        var factory = new HealthResponseFactory(new FixedTimeProvider(expectedTime));

        var response = factory.Create();

        Assert.Equal("healthy", response.Status);
        Assert.Equal("PiCommandStrip.App", response.ApplicationName);
        Assert.Equal(expectedTime, response.TimestampUtc);
        Assert.Equal(TimeSpan.Zero, response.TimestampUtc.Offset);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
