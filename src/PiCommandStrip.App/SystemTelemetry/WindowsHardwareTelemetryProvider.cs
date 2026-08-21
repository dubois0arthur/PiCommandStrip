using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace PiCommandStrip.App.SystemTelemetry;

internal sealed class WindowsHardwareTelemetryProvider(
    TimeProvider timeProvider,
    ILogger<WindowsHardwareTelemetryProvider> logger) : IHardwareTelemetryProvider
{
    internal const string ProviderDisplayName = "LibreHardwareMonitor 0.9.6 + Windows native metrics";
    private static readonly TimeSpan HardwareRetryDelay = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private Computer? _computer;
    private CpuTimeSample? _previousCpuTimes;
    private DateTimeOffset _retryHardwareAfterUtc = DateTimeOffset.MinValue;
    private string? _lastFailureType;
    private bool _disposed;

    public HardwareTelemetrySnapshot Read()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var observedAtUtc = timeProvider.GetUtcNow();
            var reasons = new List<string>();
            var cpuUtilization = ReadCpuUtilization(reasons);
            var (memoryUsed, memoryTotal) = ReadMemory(reasons);
            var devices = ReadHardwareDevices(observedAtUtc, reasons, out var providerActive);

            return new HardwareTelemetrySnapshot(
                ProviderDisplayName,
                providerActive,
                cpuUtilization,
                memoryUsed,
                memoryTotal,
                devices,
                reasons,
                observedAtUtc);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CloseComputer();
        }
    }

    private IReadOnlyList<HardwareTelemetryDeviceSnapshot> ReadHardwareDevices(
        DateTimeOffset observedAtUtc,
        List<string> reasons,
        out bool providerActive)
    {
        providerActive = false;
        if (!OperatingSystem.IsWindows())
        {
            reasons.Add("Hardware sensors require Windows.");
            return [];
        }

        if (_computer is null && observedAtUtc < _retryHardwareAfterUtc)
        {
            reasons.Add("Hardware sensor provider is waiting to retry.");
            return [];
        }

        try
        {
            EnsureComputerOpen();
            var devices = new List<HardwareTelemetryDeviceSnapshot>();
            foreach (var hardware in _computer!.Hardware)
            {
                if (!TryMapDeviceKind(hardware.HardwareType, out var deviceKind))
                {
                    continue;
                }

                hardware.Update();
                devices.Add(new HardwareTelemetryDeviceSnapshot(
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    deviceKind,
                    hardware.Sensors
                        .Select(MapSensor)
                        .Where(sensor => sensor is not null)
                        .Cast<HardwareTelemetrySensorSnapshot>()
                        .ToArray()));
            }

            providerActive = true;
            if (_lastFailureType is not null)
            {
                logger.LogInformation("System telemetry hardware sensor provider recovered");
                _lastFailureType = null;
            }
            return devices;
        }
        catch (Exception exception)
        {
            var failureType = exception.GetType().Name;
            if (!string.Equals(_lastFailureType, failureType, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    exception,
                    "System telemetry hardware sensor provider is unavailable; native CPU and memory metrics will continue");
                _lastFailureType = failureType;
            }
            reasons.Add("LibreHardwareMonitor sensors are unavailable; administrator access or compatible hardware may be required.");
            _retryHardwareAfterUtc = observedAtUtc + HardwareRetryDelay;
            CloseComputer();
            return [];
        }
    }

    private void EnsureComputerOpen()
    {
        if (_computer is not null)
        {
            return;
        }

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsBatteryEnabled = false,
            IsControllerEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsNetworkEnabled = false,
            IsPowerMonitorEnabled = false,
            IsPsuEnabled = false,
            IsStorageEnabled = false
        };
        try
        {
            computer.Open();
            _computer = computer;
        }
        catch
        {
            try
            {
                computer.Close();
            }
            catch
            {
                // Preserve the initialization failure; there is no opened provider to retain.
            }
            throw;
        }
    }

    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        if (computer is null)
        {
            return;
        }
        try
        {
            computer.Close();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not close the system telemetry hardware provider cleanly");
        }
    }

    private static HardwareTelemetrySensorSnapshot? MapSensor(ISensor sensor)
    {
        var kind = sensor.SensorType switch
        {
            SensorType.Temperature => HardwareTelemetrySensorKind.Temperature,
            SensorType.Load => HardwareTelemetrySensorKind.Load,
            SensorType.SmallData => HardwareTelemetrySensorKind.SmallData,
            _ => (HardwareTelemetrySensorKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        var value = sensor.Value is { } rawValue && float.IsFinite(rawValue)
            ? rawValue
            : (double?)null;
        return new HardwareTelemetrySensorSnapshot(
            sensor.Identifier.ToString(),
            sensor.Name,
            kind.Value,
            value);
    }

    private static bool TryMapDeviceKind(
        HardwareType hardwareType,
        out HardwareTelemetryDeviceKind deviceKind)
    {
        deviceKind = hardwareType switch
        {
            HardwareType.Cpu => HardwareTelemetryDeviceKind.Cpu,
            HardwareType.GpuAmd => HardwareTelemetryDeviceKind.GpuAmd,
            HardwareType.GpuNvidia => HardwareTelemetryDeviceKind.GpuNvidia,
            HardwareType.GpuIntel => HardwareTelemetryDeviceKind.GpuIntel,
            _ => HardwareTelemetryDeviceKind.GpuOther
        };
        return hardwareType is HardwareType.Cpu or HardwareType.GpuAmd or
            HardwareType.GpuNvidia or HardwareType.GpuIntel;
    }

    private double? ReadCpuUtilization(List<string> reasons)
    {
        if (!OperatingSystem.IsWindows() ||
            !NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            reasons.Add("Windows CPU timing information is unavailable.");
            return null;
        }

        var current = new CpuTimeSample(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
        var previous = _previousCpuTimes;
        _previousCpuTimes = current;
        if (previous is null)
        {
            return null;
        }

        var currentTotal = current.Kernel + current.User;
        var previousTotal = previous.Value.Kernel + previous.Value.User;
        if (currentTotal <= previousTotal || current.Idle < previous.Value.Idle)
        {
            return null;
        }

        var totalDelta = currentTotal - previousTotal;
        var idleDelta = current.Idle - previous.Value.Idle;
        if (totalDelta == 0)
        {
            return null;
        }
        return 100d * Math.Clamp((double)(totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta, 0, 1);
    }

    private static (long? Used, long? Total) ReadMemory(List<string> reasons)
    {
        if (!OperatingSystem.IsWindows())
        {
            reasons.Add("Windows physical-memory information is unavailable.");
            return (null, null);
        }

        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) ||
            status.TotalPhysicalMemory == 0 ||
            status.TotalPhysicalMemory > long.MaxValue)
        {
            reasons.Add("Windows physical-memory information is unavailable.");
            return (null, null);
        }

        var available = Math.Min(status.AvailablePhysicalMemory, status.TotalPhysicalMemory);
        return (
            (long)(status.TotalPhysicalMemory - available),
            (long)status.TotalPhysicalMemory);
    }

    private readonly record struct CpuTimeSample(ulong Idle, ulong Kernel, ulong User);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out NativeFileTime idleTime,
            out NativeFileTime kernelTime,
            out NativeFileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeFileTime
        {
            public uint LowDateTime;
            public uint HighDateTime;

            public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysicalMemory;
            public ulong AvailablePhysicalMemory;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtualMemory;
            public ulong AvailableVirtualMemory;
            public ulong AvailableExtendedVirtualMemory;

            public static MemoryStatusEx Create() => new()
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };
        }
    }
}
