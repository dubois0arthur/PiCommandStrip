namespace PiCommandStrip.App.Health;

public sealed record HealthResponse(
    string Status,
    string ApplicationName,
    DateTimeOffset TimestampUtc);
