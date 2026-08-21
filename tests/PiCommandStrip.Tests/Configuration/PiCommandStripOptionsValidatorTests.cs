using System.Net;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.Tests.Configuration;

public sealed class PiCommandStripOptionsValidatorTests
{
    [Fact]
    public void ValidateSystemTelemetry_AcceptsAdjustableThresholdsAndPreferredGpu()
    {
        var configuration = PiCommandStripOptionsValidator.ValidateSystemTelemetry(
            new SystemTelemetryOptions
            {
                PollIntervalMilliseconds = 1_500,
                PreferredGpu = " gpu-amd/0 ",
                CpuElevatedTemperatureCelsius = 70,
                CpuWarningTemperatureCelsius = 88,
                GpuElevatedTemperatureCelsius = 72,
                GpuWarningTemperatureCelsius = 90
            });

        Assert.Equal(TimeSpan.FromMilliseconds(1_500), configuration.PollInterval);
        Assert.Equal("gpu-amd/0", configuration.PreferredGpu);
        Assert.Equal(88, configuration.CpuWarningTemperatureCelsius);
    }

    [Theory]
    [InlineData(499, 75, 90)]
    [InlineData(1000, 90, 90)]
    [InlineData(1000, 91, 90)]
    public void ValidateSystemTelemetry_RejectsInvalidRateOrCpuThresholds(
        int interval,
        double elevated,
        double warning)
    {
        var exception = Record.Exception(() =>
            PiCommandStripOptionsValidator.ValidateSystemTelemetry(
                new SystemTelemetryOptions
                {
                    PollIntervalMilliseconds = interval,
                    CpuElevatedTemperatureCelsius = elevated,
                    CpuWarningTemperatureCelsius = warning
                }));

        Assert.NotNull(exception);
        Assert.True(exception is ArgumentOutOfRangeException or InvalidOperationException);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(750, 750)]
    [InlineData(10_000, 10_000)]
    public void ValidateCommandCooldown_ReturnsConfiguredDuration(int milliseconds, double expectedMilliseconds)
    {
        var duration = PiCommandStripOptionsValidator.ValidateCommandCooldown(new CommandOptions
        {
            CooldownMilliseconds = milliseconds
        });

        Assert.Equal(expectedMilliseconds, duration.TotalMilliseconds);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(10_001)]
    public void ValidateCommandCooldown_RejectsUnsafeValues(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PiCommandStripOptionsValidator.ValidateCommandCooldown(new CommandOptions
            {
                CooldownMilliseconds = milliseconds
            }));
    }

    [Fact]
    public void ValidateNetwork_DevelopmentModeAcceptsOnlyLoopback()
    {
        var valid = PiCommandStripOptionsValidator.ValidateNetwork(new NetworkOptions
        {
            LanEnabled = false,
            ListenAddress = "127.0.0.1",
            Port = 5077
        });

        Assert.Equal(IPAddress.Loopback, valid.ListenAddress);
        Assert.Throws<InvalidOperationException>(() =>
            PiCommandStripOptionsValidator.ValidateNetwork(new NetworkOptions
            {
                LanEnabled = false,
                ListenAddress = "192.168.1.42",
                Port = 5077
            }));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void ValidateNetwork_LanModeRejectsLoopbackAndWildcardAddresses(string address)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PiCommandStripOptionsValidator.ValidateNetwork(new NetworkOptions
            {
                LanEnabled = true,
                ListenAddress = address,
                Port = 5077
            }));
    }

    [Fact]
    public void ValidateNetwork_LanModeReturnsUsableConfiguredUrl()
    {
        var options = PiCommandStripOptionsValidator.ValidateNetwork(new NetworkOptions
        {
            LanEnabled = true,
            ListenAddress = "192.168.1.42",
            Port = 5099
        });

        Assert.Equal("http://192.168.1.42:5099", options.DashboardUrl);
    }
}
