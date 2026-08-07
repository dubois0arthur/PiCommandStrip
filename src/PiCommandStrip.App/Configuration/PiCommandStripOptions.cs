using System.Net;

namespace PiCommandStrip.App.Configuration;

public sealed class PiCommandStripOptions
{
    public NetworkOptions Network { get; init; } = new();

    public AuthenticationOptions Authentication { get; init; } = new();
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
}
