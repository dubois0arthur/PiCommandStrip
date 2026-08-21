namespace PiCommandStrip.App.SystemTelemetry;

public enum HardwareTelemetryDeviceKind
{
    Cpu,
    GpuAmd,
    GpuNvidia,
    GpuIntel,
    GpuOther
}

public enum HardwareTelemetrySensorKind
{
    Temperature,
    Load,
    SmallData
}

public sealed record HardwareTelemetrySensorSnapshot(
    string Identifier,
    string Name,
    HardwareTelemetrySensorKind Kind,
    double? Value);

public sealed record HardwareTelemetryDeviceSnapshot(
    string Identifier,
    string Name,
    HardwareTelemetryDeviceKind Kind,
    IReadOnlyList<HardwareTelemetrySensorSnapshot> Sensors);

public sealed record HardwareTelemetrySnapshot(
    string ProviderName,
    bool ProviderActive,
    double? CpuUtilizationPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    IReadOnlyList<HardwareTelemetryDeviceSnapshot> Devices,
    IReadOnlyList<string> UnavailableReasons,
    DateTimeOffset ObservedAtUtc);

internal interface IHardwareTelemetryProvider : IDisposable
{
    HardwareTelemetrySnapshot Read();
}
