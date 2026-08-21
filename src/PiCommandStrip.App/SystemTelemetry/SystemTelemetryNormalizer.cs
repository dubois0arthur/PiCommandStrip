using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.App.SystemTelemetry;

public sealed class SystemTelemetryNormalizer(SystemTelemetryConfiguration configuration)
{
    private const long MemoryMeaningfulStepBytes = 16L * 1024 * 1024;
    private const double MebibyteBytes = 1024d * 1024d;

    public SystemTelemetryState Normalize(HardwareTelemetrySnapshot snapshot)
    {
        var reasons = snapshot.UnavailableReasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var cpuDevice = SelectCpuDevice(snapshot.Devices);
        var cpuTemperatureSensor = SelectCpuTemperatureSensor(cpuDevice?.Sensors ?? []);
        var cpuUtilization = NormalizePercent(snapshot.CpuUtilizationPercent);
        var cpuTemperature = NormalizeTemperature(cpuTemperatureSensor?.Value);
        AddMissingReason(reasons, cpuUtilization, "CPU utilization is unavailable.");
        AddMissingReason(reasons, cpuTemperature, "CPU package temperature sensor is unavailable.");
        var cpu = cpuDevice is null && cpuUtilization is null && cpuTemperature is null
            ? null
            : new CpuTelemetryState(
                cpuDevice?.Name,
                cpuUtilization,
                cpuTemperature,
                GetTemperatureStatus(
                    cpuTemperature,
                    configuration.CpuElevatedTemperatureCelsius,
                    configuration.CpuWarningTemperatureCelsius));

        var gpuDevice = SelectGpuDevice(snapshot.Devices, configuration.PreferredGpu);
        var gpuTemperatureSensor = SelectGpuTemperatureSensor(gpuDevice?.Sensors ?? []);
        var gpuLoadSensor = SelectGpuLoadSensor(gpuDevice?.Sensors ?? []);
        var gpuMemoryUsedSensor = SelectSensorByNames(
            gpuDevice?.Sensors ?? [],
            HardwareTelemetrySensorKind.SmallData,
            "GPU Memory Used",
            "D3D Dedicated Memory Used");
        var gpuMemoryTotalSensor = SelectSensorByNames(
            gpuDevice?.Sensors ?? [],
            HardwareTelemetrySensorKind.SmallData,
            "GPU Memory Total",
            "D3D Dedicated Memory Total");
        var gpuMemoryFreeSensor = SelectSensorByNames(
            gpuDevice?.Sensors ?? [],
            HardwareTelemetrySensorKind.SmallData,
            "GPU Memory Free",
            "D3D Dedicated Memory Free");
        var gpuTemperature = NormalizeTemperature(gpuTemperatureSensor?.Value);
        var gpuUtilization = NormalizePercent(gpuLoadSensor?.Value);
        var gpuMemoryUsed = SmallDataToBytes(gpuMemoryUsedSensor?.Value);
        var gpuMemoryTotal = SmallDataToBytes(gpuMemoryTotalSensor?.Value);
        if (gpuMemoryTotal is null && gpuMemoryUsed is not null)
        {
            var free = SmallDataToBytes(gpuMemoryFreeSensor?.Value);
            if (free is not null && gpuMemoryUsed <= long.MaxValue - free)
            {
                gpuMemoryTotal = gpuMemoryUsed + free;
            }
        }

        if (gpuMemoryUsed is not null && gpuMemoryTotal is not null)
        {
            gpuMemoryUsed = Math.Min(gpuMemoryUsed.Value, gpuMemoryTotal.Value);
        }

        if (gpuDevice is null)
        {
            reasons.Add("No supported GPU was detected.");
        }
        else
        {
            AddMissingReason(reasons, gpuUtilization, "GPU utilization sensor is unavailable.");
            AddMissingReason(reasons, gpuTemperature, "GPU core temperature sensor is unavailable.");
            AddMissingReason(reasons, gpuMemoryUsed, "GPU memory-used sensor is unavailable.");
            AddMissingReason(reasons, gpuMemoryTotal, "GPU memory-total sensor is unavailable.");
        }

        var gpu = gpuDevice is null
            ? null
            : new GpuTelemetryState(
                gpuDevice.Identifier,
                gpuDevice.Name,
                gpuUtilization,
                gpuTemperature,
                gpuMemoryUsed,
                gpuMemoryTotal,
                GetTemperatureStatus(
                    gpuTemperature,
                    configuration.GpuElevatedTemperatureCelsius,
                    configuration.GpuWarningTemperatureCelsius));

        var memoryTotal = NormalizeMemoryBytes(snapshot.MemoryTotalBytes);
        var memoryUsed = NormalizeMemoryBytes(snapshot.MemoryUsedBytes);
        if (memoryUsed is not null && memoryTotal is not null)
        {
            memoryUsed = Math.Min(memoryUsed.Value, memoryTotal.Value);
        }
        AddMissingReason(reasons, memoryUsed, "System memory usage is unavailable.");
        AddMissingReason(reasons, memoryTotal, "System memory capacity is unavailable.");
        var memory = memoryUsed is null && memoryTotal is null
            ? null
            : new MemoryTelemetryState(memoryUsed, memoryTotal);

        var availableMetricCount = CountAvailableMetrics(
            cpuUtilization,
            cpuTemperature,
            gpuUtilization,
            gpuTemperature,
            gpuMemoryUsed,
            gpuMemoryTotal,
            memoryUsed,
            memoryTotal);
        var status = availableMetricCount switch
        {
            0 => SystemTelemetryStatuses.Unavailable,
            8 => SystemTelemetryStatuses.Available,
            _ => SystemTelemetryStatuses.Partial
        };
        var providerStatus = snapshot.ProviderActive
            ? reasons.Count == 0 ? "active" : "partial"
            : availableMetricCount > 0 ? "degraded" : "unavailable";
        var cpuStatus = cpu?.TemperatureStatus ?? TelemetryTemperatureStatuses.Unavailable;
        var gpuStatus = gpu?.TemperatureStatus ?? TelemetryTemperatureStatuses.Unavailable;
        var temperatureStatus = MaxTemperatureStatus(cpuStatus, gpuStatus);

        return new SystemTelemetryState(
            status,
            cpu,
            gpu,
            memory,
            temperatureStatus,
            0,
            snapshot.ObservedAtUtc,
            new SystemTelemetryDiagnosticsState(
                snapshot.ProviderName,
                providerStatus,
                cpuTemperatureSensor?.Name,
                gpuDevice?.Identifier,
                gpuDevice?.Name,
                gpuTemperatureSensor?.Name,
                reasons.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static HardwareTelemetryDeviceSnapshot? SelectCpuDevice(
        IReadOnlyList<HardwareTelemetryDeviceSnapshot> devices) =>
        devices
            .Where(device => device.Kind == HardwareTelemetryDeviceKind.Cpu)
            .OrderByDescending(device => SelectCpuTemperatureSensor(device.Sensors) is not null)
            .ThenBy(device => device.Identifier, StringComparer.Ordinal)
            .FirstOrDefault();

    private static HardwareTelemetryDeviceSnapshot? SelectGpuDevice(
        IReadOnlyList<HardwareTelemetryDeviceSnapshot> devices,
        string? preferredGpu)
    {
        var candidates = devices.Where(device => device.Kind is
            HardwareTelemetryDeviceKind.GpuAmd or
            HardwareTelemetryDeviceKind.GpuNvidia or
            HardwareTelemetryDeviceKind.GpuIntel or
            HardwareTelemetryDeviceKind.GpuOther).ToArray();

        if (!string.IsNullOrWhiteSpace(preferredGpu))
        {
            var preferred = candidates.FirstOrDefault(device =>
                string.Equals(device.Identifier, preferredGpu, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.Name, preferredGpu, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return candidates
            .OrderByDescending(GetGpuSelectionScore)
            .ThenBy(device => device.Identifier, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int GetGpuSelectionScore(HardwareTelemetryDeviceSnapshot device)
    {
        var sensors = device.Sensors;
        var score = device.Kind switch
        {
            HardwareTelemetryDeviceKind.GpuNvidia => 120,
            HardwareTelemetryDeviceKind.GpuAmd => 110,
            HardwareTelemetryDeviceKind.GpuIntel => 20,
            _ => 0
        };
        var dedicatedMemoryTotal = SelectSensorByNames(
                sensors,
                HardwareTelemetrySensorKind.SmallData,
                "GPU Memory Total",
                "D3D Dedicated Memory Total");
        if (dedicatedMemoryTotal?.Value is { } memoryMebibytes && memoryMebibytes > 0)
        {
            score += 1_000 + (int)Math.Min(2_000, memoryMebibytes / 16);
        }
        if (SelectGpuTemperatureSensor(sensors) is not null)
        {
            score += 50;
        }
        if (SelectGpuLoadSensor(sensors) is not null)
        {
            score += 25;
        }
        return score;
    }

    private static HardwareTelemetrySensorSnapshot? SelectCpuTemperatureSensor(
        IReadOnlyList<HardwareTelemetrySensorSnapshot> sensors) =>
        sensors
            .Where(sensor => sensor.Kind == HardwareTelemetrySensorKind.Temperature &&
                NormalizeTemperature(sensor.Value) is not null)
            .Select(sensor => (Sensor: sensor, Priority: GetCpuTemperaturePriority(sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Identifier, StringComparer.Ordinal)
            .Select(candidate => candidate.Sensor)
            .FirstOrDefault();

    private static int GetCpuTemperaturePriority(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)) return 98;
        if (name.Equals("CPU (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)) return 97;
        if (name.Equals("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return 96;
        if (name.Equals("Core Average", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Equals("CCDs Average (Tdie)", StringComparison.OrdinalIgnoreCase)) return 88;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Distance", StringComparison.OrdinalIgnoreCase)) return 80;
        return 0;
    }

    private static HardwareTelemetrySensorSnapshot? SelectGpuTemperatureSensor(
        IReadOnlyList<HardwareTelemetrySensorSnapshot> sensors) =>
        SelectSensorByNames(
            sensors,
            HardwareTelemetrySensorKind.Temperature,
            "GPU Core");

    private static HardwareTelemetrySensorSnapshot? SelectGpuLoadSensor(
        IReadOnlyList<HardwareTelemetrySensorSnapshot> sensors) =>
        SelectSensorByNames(
            sensors,
            HardwareTelemetrySensorKind.Load,
            "GPU Core",
            "D3D 3D");

    private static HardwareTelemetrySensorSnapshot? SelectSensorByNames(
        IReadOnlyList<HardwareTelemetrySensorSnapshot> sensors,
        HardwareTelemetrySensorKind kind,
        params string[] preferredNames)
    {
        for (var index = 0; index < preferredNames.Length; index++)
        {
            var sensor = sensors
                .Where(candidate => candidate.Kind == kind &&
                    string.Equals(candidate.Name, preferredNames[index], StringComparison.OrdinalIgnoreCase) &&
                    candidate.Value is not null && double.IsFinite(candidate.Value.Value))
                .OrderBy(candidate => candidate.Identifier, StringComparer.Ordinal)
                .FirstOrDefault();
            if (sensor is not null)
            {
                return sensor;
            }
        }
        return null;
    }

    private static double? NormalizePercent(double? value) =>
        value is null || !double.IsFinite(value.Value)
            ? null
            : Math.Round(Math.Clamp(value.Value, 0, 100), 1, MidpointRounding.AwayFromZero);

    private static double? NormalizeTemperature(double? value) =>
        value is null || !double.IsFinite(value.Value) || value <= 0 || value > 150
            ? null
            : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);

    private static long? SmallDataToBytes(double? value)
    {
        if (value is null || !double.IsFinite(value.Value) || value < 0 ||
            value > long.MaxValue / MebibyteBytes)
        {
            return null;
        }
        return (long)Math.Round(value.Value * MebibyteBytes, MidpointRounding.AwayFromZero);
    }

    private static long? NormalizeMemoryBytes(long? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }
        return value.Value / MemoryMeaningfulStepBytes * MemoryMeaningfulStepBytes;
    }

    private static string GetTemperatureStatus(double? temperature, double elevated, double warning) =>
        temperature is null
            ? TelemetryTemperatureStatuses.Unavailable
            : temperature >= warning
                ? TelemetryTemperatureStatuses.Warning
                : temperature >= elevated
                    ? TelemetryTemperatureStatuses.Elevated
                    : TelemetryTemperatureStatuses.Normal;

    private static string MaxTemperatureStatus(string left, string right)
    {
        static int Rank(string status) => status switch
        {
            TelemetryTemperatureStatuses.Warning => 3,
            TelemetryTemperatureStatuses.Elevated => 2,
            TelemetryTemperatureStatuses.Normal => 1,
            _ => 0
        };
        return Rank(left) >= Rank(right) ? left : right;
    }

    private static int CountAvailableMetrics(params object?[] metrics) =>
        metrics.Count(metric => metric is not null);

    private static void AddMissingReason<T>(List<string> reasons, T? value, string reason)
    {
        if (value is null)
        {
            reasons.Add(reason);
        }
    }
}
