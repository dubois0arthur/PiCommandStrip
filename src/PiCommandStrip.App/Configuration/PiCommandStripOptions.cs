using System.Net;

namespace PiCommandStrip.App.Configuration;

public sealed class PiCommandStripOptions
{
    public NetworkOptions Network { get; init; } = new();

    public AuthenticationOptions Authentication { get; init; } = new();

    public CommandOptions Commands { get; init; } = new();

    public ContextOptions Contexts { get; init; } = new();

    public SpotifyOptions Spotify { get; init; } = new();

    public BrowserIntegrationOptions BrowserIntegration { get; init; } = new();

    public SystemTelemetryOptions SystemTelemetry { get; init; } = new();
}

public sealed class NetworkOptions
{
    public bool LanEnabled { get; init; }

    public string ListenAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 5077;
}

public sealed class AuthenticationOptions
{
    public string Token { get; init; } = string.Empty;
}

public sealed class CommandOptions
{
    public int CooldownMilliseconds { get; init; } = 750;
}

public sealed class ContextOptions
{
    public Dictionary<string, string[]> ProcessMappings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SpotifyOptions
{
    public bool Enabled { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = string.Empty;
}

public sealed class BrowserIntegrationOptions
{
    public bool Enabled { get; init; }

    public int Port { get; init; } = 5078;

    public string Token { get; init; } = string.Empty;

    public Dictionary<string, BrowserSearchActionOptions> SearchActions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BrowserSearchActionOptions
{
    public string DisplayName { get; init; } = string.Empty;

    public string UrlTemplate { get; init; } = string.Empty;
}

public sealed class SystemTelemetryOptions
{
    public bool Enabled { get; init; } = true;

    public int PollIntervalMilliseconds { get; init; } = 1_000;

    public string PreferredGpu { get; init; } = string.Empty;

    public double CpuElevatedTemperatureCelsius { get; init; } = 75;

    public double CpuWarningTemperatureCelsius { get; init; } = 90;

    public double GpuElevatedTemperatureCelsius { get; init; } = 75;

    public double GpuWarningTemperatureCelsius { get; init; } = 85;
}

public sealed record SystemTelemetryConfiguration(
    bool Enabled,
    TimeSpan PollInterval,
    string? PreferredGpu,
    double CpuElevatedTemperatureCelsius,
    double CpuWarningTemperatureCelsius,
    double GpuElevatedTemperatureCelsius,
    double GpuWarningTemperatureCelsius);

public sealed record ValidatedNetworkOptions(bool LanEnabled, IPAddress ListenAddress, int Port)
{
    public string DashboardUrl
    {
        get
        {
            var host = LanEnabled
                ? ListenAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? $"[{ListenAddress}]"
                    : ListenAddress.ToString()
                : "localhost";

            return $"http://{host}:{Port}";
        }
    }
}

public static class PiCommandStripOptionsValidator
{
    public static TimeSpan ValidateCommandCooldown(CommandOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CooldownMilliseconds, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.CooldownMilliseconds, 10_000);

        return TimeSpan.FromMilliseconds(options.CooldownMilliseconds);
    }

    public static ValidatedNetworkOptions ValidateNetwork(NetworkOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, 65535);

        if (!IPAddress.TryParse(options.ListenAddress, out var listenAddress))
        {
            throw new InvalidOperationException(
                "PiCommandStrip:Network:ListenAddress must be an explicit IP address.");
        }

        if (options.LanEnabled)
        {
            if (IPAddress.IsLoopback(listenAddress) ||
                listenAddress.Equals(IPAddress.Any) ||
                listenAddress.Equals(IPAddress.IPv6Any))
            {
                throw new InvalidOperationException(
                    "LAN mode requires the PC's explicit non-loopback IP address; wildcard addresses are not accepted.");
            }
        }
        else if (!IPAddress.IsLoopback(listenAddress))
        {
            throw new InvalidOperationException(
                "Development mode must use a loopback listen address. Enable the Lan configuration for network access.");
        }

        return new ValidatedNetworkOptions(options.LanEnabled, listenAddress, options.Port);
    }

    public static SystemTelemetryConfiguration ValidateSystemTelemetry(
        SystemTelemetryOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PollIntervalMilliseconds, 500);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.PollIntervalMilliseconds, 10_000);
        ValidateTemperatureThresholds(
            options.CpuElevatedTemperatureCelsius,
            options.CpuWarningTemperatureCelsius,
            "CPU");
        ValidateTemperatureThresholds(
            options.GpuElevatedTemperatureCelsius,
            options.GpuWarningTemperatureCelsius,
            "GPU");

        var preferredGpu = string.IsNullOrWhiteSpace(options.PreferredGpu)
            ? null
            : options.PreferredGpu.Trim();
        if (preferredGpu?.Length > 200)
        {
            throw new InvalidOperationException(
                "PiCommandStrip:SystemTelemetry:PreferredGpu must be 200 characters or fewer.");
        }

        return new SystemTelemetryConfiguration(
            options.Enabled,
            TimeSpan.FromMilliseconds(options.PollIntervalMilliseconds),
            preferredGpu,
            options.CpuElevatedTemperatureCelsius,
            options.CpuWarningTemperatureCelsius,
            options.GpuElevatedTemperatureCelsius,
            options.GpuWarningTemperatureCelsius);
    }

    private static void ValidateTemperatureThresholds(
        double elevated,
        double warning,
        string metricName)
    {
        if (!double.IsFinite(elevated) || !double.IsFinite(warning) ||
            elevated < 1 || elevated > 150 || warning < 1 || warning > 150 ||
            warning <= elevated)
        {
            throw new InvalidOperationException(
                $"{metricName} telemetry thresholds must be finite values from 1 through 150 °C, with Warning greater than Elevated.");
        }
    }
}
