namespace PiCommandStrip.App.SystemTelemetry;

public static class SystemTelemetryStatuses
{
    public const string Available = "available";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
}

public static class TelemetryTemperatureStatuses
{
    public const string Normal = "normal";
    public const string Elevated = "elevated";
    public const string Warning = "warning";
    public const string Unavailable = "unavailable";
}

public sealed record CpuTelemetryState(
    string? Name,
    double? UtilizationPercent,
    double? TemperatureCelsius,
    string TemperatureStatus);

public sealed record GpuTelemetryState(
    string? Identifier,
    string? Name,
    double? UtilizationPercent,
    double? TemperatureCelsius,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    string TemperatureStatus);

public sealed record MemoryTelemetryState(long? UsedBytes, long? TotalBytes);

public sealed record SystemTelemetryDiagnosticsState(
    string ProviderName,
    string ProviderStatus,
    string? CpuTemperatureSensor,
    string? GpuIdentifier,
    string? GpuName,
    string? GpuTemperatureSensor,
    IReadOnlyList<string> UnavailableReasons)
{
    public bool HasSameMeaningAs(SystemTelemetryDiagnosticsState other) =>
        string.Equals(ProviderName, other.ProviderName, StringComparison.Ordinal) &&
        string.Equals(ProviderStatus, other.ProviderStatus, StringComparison.Ordinal) &&
        string.Equals(CpuTemperatureSensor, other.CpuTemperatureSensor, StringComparison.Ordinal) &&
        string.Equals(GpuIdentifier, other.GpuIdentifier, StringComparison.Ordinal) &&
        string.Equals(GpuName, other.GpuName, StringComparison.Ordinal) &&
        string.Equals(GpuTemperatureSensor, other.GpuTemperatureSensor, StringComparison.Ordinal) &&
        UnavailableReasons.SequenceEqual(other.UnavailableReasons, StringComparer.Ordinal);
}

public sealed record SystemTelemetryState(
    string Status,
    CpuTelemetryState? Cpu,
    GpuTelemetryState? Gpu,
    MemoryTelemetryState? Memory,
    string TemperatureStatus,
    long Revision,
    DateTimeOffset LastUpdatedUtc,
    SystemTelemetryDiagnosticsState Diagnostics)
{
    public static SystemTelemetryState Unavailable(
        DateTimeOffset lastUpdatedUtc,
        string providerName = "LibreHardwareMonitor + Windows") =>
        new(
            SystemTelemetryStatuses.Unavailable,
            null,
            null,
            null,
            TelemetryTemperatureStatuses.Unavailable,
            0,
            lastUpdatedUtc,
            new SystemTelemetryDiagnosticsState(
                providerName,
                "initializing",
                null,
                null,
                null,
                null,
                ["Telemetry has not been sampled yet."]));

    public bool HasSameMeaningAs(SystemTelemetryState other) =>
        string.Equals(Status, other.Status, StringComparison.Ordinal) &&
        Equals(Cpu, other.Cpu) &&
        Equals(Gpu, other.Gpu) &&
        Equals(Memory, other.Memory) &&
        string.Equals(TemperatureStatus, other.TemperatureStatus, StringComparison.Ordinal) &&
        Diagnostics.HasSameMeaningAs(other.Diagnostics);
}
