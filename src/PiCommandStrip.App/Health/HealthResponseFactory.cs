namespace PiCommandStrip.App.Health;

public sealed class HealthResponseFactory(TimeProvider timeProvider)
{
    public HealthResponse Create() => new(
        Status: "healthy",
        ApplicationName: "PiCommandStrip.App",
        TimestampUtc: timeProvider.GetUtcNow());
}
