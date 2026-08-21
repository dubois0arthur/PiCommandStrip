using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.SystemTelemetry;

namespace PiCommandStrip.Tests.SystemTelemetry;

public sealed class SystemTelemetryNormalizerTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_SelectsPackageAndCoreSensorsWithoutUsingHotspot()
    {
        var snapshot = Snapshot(
            cpuUtilization: 18.24,
            memoryUsed: 18L * 1024 * 1024 * 1024,
            memoryTotal: 32L * 1024 * 1024 * 1024,
            devices:
            [
                Cpu(
                    Sensor("cpu/core-average", "Core Average", HardwareTelemetrySensorKind.Temperature, 51),
                    Sensor("cpu/package", "CPU Package", HardwareTelemetrySensorKind.Temperature, 54)),
                Gpu(
                    "gpu-nvidia/0",
                    "NVIDIA GeForce RTX",
                    HardwareTelemetryDeviceKind.GpuNvidia,
                    Sensor("gpu/temp-hotspot", "GPU Hot Spot", HardwareTelemetrySensorKind.Temperature, 91),
                    Sensor("gpu/temp-core", "GPU Core", HardwareTelemetrySensorKind.Temperature, 67),
                    Sensor("gpu/load-core", "GPU Core", HardwareTelemetrySensorKind.Load, 92),
                    Sensor("gpu/memory-used", "GPU Memory Used", HardwareTelemetrySensorKind.SmallData, 4096),
                    Sensor("gpu/memory-total", "GPU Memory Total", HardwareTelemetrySensorKind.SmallData, 12288))
            ]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal(SystemTelemetryStatuses.Available, state.Status);
        Assert.Equal(18.2, state.Cpu!.UtilizationPercent);
        Assert.Equal(54, state.Cpu.TemperatureCelsius);
        Assert.Equal("CPU Package", state.Diagnostics.CpuTemperatureSensor);
        Assert.Equal(67, state.Gpu!.TemperatureCelsius);
        Assert.Equal(92, state.Gpu.UtilizationPercent);
        Assert.Equal(4L * 1024 * 1024 * 1024, state.Gpu.MemoryUsedBytes);
        Assert.Equal(12L * 1024 * 1024 * 1024, state.Gpu.MemoryTotalBytes);
        Assert.Equal("GPU Core", state.Diagnostics.GpuTemperatureSensor);
        Assert.Empty(state.Diagnostics.UnavailableReasons);
    }

    [Fact]
    public void Normalize_AmdCpuUsesTctlTdieAggregateWhenPackageIsAbsent()
    {
        var snapshot = Snapshot(devices:
        [
            Cpu(
                Sensor("cpu/ccd1", "CCD1 (Tdie)", HardwareTelemetrySensorKind.Temperature, 49),
                Sensor("cpu/tctl", "Core (Tctl/Tdie)", HardwareTelemetrySensorKind.Temperature, 58))
        ]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal(58, state.Cpu!.TemperatureCelsius);
        Assert.Equal("Core (Tctl/Tdie)", state.Diagnostics.CpuTemperatureSensor);
    }

    [Fact]
    public void Normalize_MultipleGpusPrefersAdapterWithDedicatedMemory()
    {
        var snapshot = Snapshot(devices:
        [
            Gpu(
                "gpu-intel/0",
                "Intel Integrated Graphics",
                HardwareTelemetryDeviceKind.GpuIntel,
                Sensor("intel/temp", "GPU Core", HardwareTelemetrySensorKind.Temperature, 42),
                Sensor("intel/load", "D3D 3D", HardwareTelemetrySensorKind.Load, 5)),
            Gpu(
                "gpu-amd/0",
                "AMD Radeon RX 7900 XTX",
                HardwareTelemetryDeviceKind.GpuAmd,
                Sensor("amd/temp", "GPU Core", HardwareTelemetrySensorKind.Temperature, 64),
                Sensor("amd/load", "GPU Core", HardwareTelemetrySensorKind.Load, 88),
                Sensor("amd/used", "GPU Memory Used", HardwareTelemetrySensorKind.SmallData, 5000),
                Sensor("amd/total", "GPU Memory Total", HardwareTelemetrySensorKind.SmallData, 24576))
        ]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal("gpu-amd/0", state.Gpu!.Identifier);
        Assert.Equal("AMD Radeon RX 7900 XTX", state.Gpu.Name);
    }

    [Fact]
    public void Normalize_ConfiguredGpuRequiresExactIdentifierOrName()
    {
        var snapshot = Snapshot(devices:
        [
            Gpu(
                "gpu-intel/0",
                "Intel Arc A770",
                HardwareTelemetryDeviceKind.GpuIntel,
                Sensor("intel/temp", "GPU Core", HardwareTelemetrySensorKind.Temperature, 50)),
            Gpu(
                "gpu-nvidia/0",
                "NVIDIA GeForce RTX",
                HardwareTelemetryDeviceKind.GpuNvidia,
                Sensor("nvidia/temp", "GPU Core", HardwareTelemetrySensorKind.Temperature, 60),
                Sensor("nvidia/total", "GPU Memory Total", HardwareTelemetrySensorKind.SmallData, 12288))
        ]);

        var state = Normalizer(preferredGpu: "gpu-intel/0").Normalize(snapshot);

        Assert.Equal("gpu-intel/0", state.Gpu!.Identifier);
    }

    [Fact]
    public void Normalize_SameVendorGpusPreferLargerDedicatedMemory()
    {
        var snapshot = Snapshot(devices:
        [
            Gpu(
                "gpu-amd/0",
                "AMD Integrated Graphics",
                HardwareTelemetryDeviceKind.GpuAmd,
                Sensor("integrated/total", "GPU Memory Total", HardwareTelemetrySensorKind.SmallData, 512)),
            Gpu(
                "gpu-amd/1",
                "AMD Radeon Discrete",
                HardwareTelemetryDeviceKind.GpuAmd,
                Sensor("discrete/total", "GPU Memory Total", HardwareTelemetrySensorKind.SmallData, 16384))
        ]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal("gpu-amd/1", state.Gpu!.Identifier);
    }

    [Fact]
    public void Normalize_MissingSensorsAndVramRemainNullWithReasons()
    {
        var snapshot = Snapshot(devices:
        [
            Gpu(
                "gpu-amd/0",
                "AMD Radeon",
                HardwareTelemetryDeviceKind.GpuAmd,
                Sensor("amd/hotspot", "GPU Hot Spot", HardwareTelemetrySensorKind.Temperature, 86),
                Sensor("amd/used", "GPU Memory Used", HardwareTelemetrySensorKind.SmallData, null))
        ]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal(SystemTelemetryStatuses.Unavailable, state.Status);
        Assert.Null(state.Gpu!.TemperatureCelsius);
        Assert.Null(state.Gpu.UtilizationPercent);
        Assert.Null(state.Gpu.MemoryUsedBytes);
        Assert.Null(state.Gpu.MemoryTotalBytes);
        Assert.Contains("GPU core temperature sensor is unavailable.", state.Diagnostics.UnavailableReasons);
        Assert.Equal(TelemetryTemperatureStatuses.Unavailable, state.Gpu.TemperatureStatus);
    }

    [Fact]
    public void Normalize_ProviderUnavailableStillKeepsNativeMetrics()
    {
        var snapshot = Snapshot(
            providerActive: false,
            cpuUtilization: 22,
            memoryUsed: 8L * 1024 * 1024 * 1024,
            memoryTotal: 16L * 1024 * 1024 * 1024,
            reasons: ["Hardware provider unavailable."]);

        var state = Normalizer().Normalize(snapshot);

        Assert.Equal(SystemTelemetryStatuses.Partial, state.Status);
        Assert.Equal(22, state.Cpu!.UtilizationPercent);
        Assert.Equal("degraded", state.Diagnostics.ProviderStatus);
        Assert.Null(state.Gpu);
        Assert.NotNull(state.Memory);
    }

    [Fact]
    public void Normalize_NoUsableDataProducesExplicitUnavailableState()
    {
        var state = Normalizer().Normalize(Snapshot(providerActive: false));

        Assert.Equal(SystemTelemetryStatuses.Unavailable, state.Status);
        Assert.Null(state.Cpu);
        Assert.Null(state.Gpu);
        Assert.Null(state.Memory);
        Assert.Equal(TelemetryTemperatureStatuses.Unavailable, state.TemperatureStatus);
        Assert.Equal("unavailable", state.Diagnostics.ProviderStatus);
    }

    [Fact]
    public void Normalize_ZeroDegreeSensorReadingIsUnavailableRatherThanNormal()
    {
        var state = Normalizer().Normalize(Snapshot(devices:
        [
            Cpu(Sensor("cpu/package", "Core (Tctl/Tdie)", HardwareTelemetrySensorKind.Temperature, 0))
        ]));

        Assert.Null(state.Cpu!.TemperatureCelsius);
        Assert.Null(state.Diagnostics.CpuTemperatureSensor);
        Assert.Contains(
            "CPU package temperature sensor is unavailable.",
            state.Diagnostics.UnavailableReasons);
    }

    [Theory]
    [InlineData(74.9, 74.9, "normal")]
    [InlineData(75.0, 84.9, "elevated")]
    [InlineData(74.9, 85.0, "warning")]
    public void Normalize_AppliesCentralTemperatureThresholds(
        double cpuTemperature,
        double gpuTemperature,
        string expectedOverallStatus)
    {
        var state = Normalizer().Normalize(Snapshot(devices:
        [
            Cpu(Sensor("cpu/package", "CPU Package", HardwareTelemetrySensorKind.Temperature, cpuTemperature)),
            Gpu(
                "gpu/0",
                "GPU",
                HardwareTelemetryDeviceKind.GpuNvidia,
                Sensor("gpu/temp", "GPU Core", HardwareTelemetrySensorKind.Temperature, gpuTemperature))
        ]));

        Assert.Equal(expectedOverallStatus, state.TemperatureStatus);
    }

    [Fact]
    public void StateStore_SuppressesNormalizedDuplicateAndRetainsMeaningfulChange()
    {
        var normalizer = Normalizer();
        var store = new SystemTelemetryStateStore(TimeProvider.System);
        var first = normalizer.Normalize(Snapshot(
            cpuUtilization: 18.21,
            memoryUsed: 8L * 1024 * 1024 * 1024 + 1,
            memoryTotal: 16L * 1024 * 1024 * 1024));
        var duplicate = normalizer.Normalize(Snapshot(
            cpuUtilization: 18.24,
            memoryUsed: 8L * 1024 * 1024 * 1024 + 2,
            memoryTotal: 16L * 1024 * 1024 * 1024));
        var changed = normalizer.Normalize(Snapshot(
            cpuUtilization: 19.2,
            memoryUsed: 8L * 1024 * 1024 * 1024 + 2,
            memoryTotal: 16L * 1024 * 1024 * 1024));

        Assert.True(store.TryUpdate(first, out var firstStored));
        Assert.False(store.TryUpdate(duplicate, out var duplicateStored));
        Assert.True(store.TryUpdate(changed, out var changedStored));
        Assert.Equal(firstStored.Revision, duplicateStored.Revision);
        Assert.Equal(firstStored.Revision + 1, changedStored.Revision);
    }

    private static SystemTelemetryNormalizer Normalizer(string? preferredGpu = null) =>
        new(new SystemTelemetryConfiguration(
            true,
            TimeSpan.FromSeconds(1),
            preferredGpu,
            75,
            90,
            75,
            85));

    private static HardwareTelemetrySnapshot Snapshot(
        bool providerActive = true,
        double? cpuUtilization = null,
        long? memoryUsed = null,
        long? memoryTotal = null,
        IReadOnlyList<HardwareTelemetryDeviceSnapshot>? devices = null,
        IReadOnlyList<string>? reasons = null) =>
        new(
            "Test provider",
            providerActive,
            cpuUtilization,
            memoryUsed,
            memoryTotal,
            devices ?? [],
            reasons ?? [],
            ObservedAt);

    private static HardwareTelemetryDeviceSnapshot Cpu(
        params HardwareTelemetrySensorSnapshot[] sensors) =>
        new("cpu/0", "AMD Ryzen", HardwareTelemetryDeviceKind.Cpu, sensors);

    private static HardwareTelemetryDeviceSnapshot Gpu(
        string identifier,
        string name,
        HardwareTelemetryDeviceKind kind,
        params HardwareTelemetrySensorSnapshot[] sensors) =>
        new(identifier, name, kind, sensors);

    private static HardwareTelemetrySensorSnapshot Sensor(
        string identifier,
        string name,
        HardwareTelemetrySensorKind kind,
        double? value) =>
        new(identifier, name, kind, value);
}
