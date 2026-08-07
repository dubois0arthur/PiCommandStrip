using System.Net;
using PiCommandStrip.App.Configuration;

namespace PiCommandStrip.Tests.Configuration;

public sealed class PiCommandStripOptionsValidatorTests
{
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
