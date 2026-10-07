using MercuryManager.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MercuryManager.Assets.Tests;

public sealed class StartupOptionsTests
{
    private static StartupOptions Parse(params string[] args) => StartupOptions.FromConfiguration(
        new ConfigurationBuilder().AddCommandLine(StartupOptions.NormalizeArguments(args)).Build());

    [Fact]
    public void DefaultsLaunchOnLoopback()
    {
        var options = Parse();
        Assert.Equal("http://127.0.0.1:5087", options.ListenUrl);
        Assert.True(options.LaunchBrowser);
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("-port")]
    public void SupportsHostPortAndStandaloneNoLaunch(string portFlag)
    {
        var options = Parse("--host", "0.0.0.0", portFlag, "8081", "--no-launch");
        Assert.Equal("http://0.0.0.0:8081", options.ListenUrl);
        Assert.False(options.LaunchBrowser);
    }

    [Fact]
    public void SupportsEqualsSyntaxAndLocalhost()
    {
        var options = Parse("--host=localhost", "-port=8081", "--no-launch=false");
        Assert.Equal("http://127.0.0.1:8081", options.ListenUrl);
        Assert.True(options.LaunchBrowser);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("-1")]
    public void RejectsInvalidPorts(string port) => Assert.Throws<ArgumentException>(() => Parse("--port=" + port));

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("0.0.0.0/path")]
    public void RejectsInvalidHosts(string host) => Assert.Throws<ArgumentException>(() => Parse("--host=" + host));

    [Fact]
    public void RetainsLegacyListenUrl()
    {
        Assert.Equal("http://127.0.0.1:8090", Parse("--listen-url", "http://127.0.0.1:8090").ListenUrl);
        Assert.Throws<ArgumentException>(() => Parse("--listen-url=http://127.0.0.1:8090", "--port=8081"));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8081", "http://127.0.0.1:8081/")]
    [InlineData("http://[::]:8081", "http://127.0.0.1:8081/")]
    [InlineData("http://192.168.1.2:8081", "http://192.168.1.2:8081/")]
    [InlineData("http://[::1]:8081", "http://[::1]:8081/")]
    public void BuildsNavigableBrowserUrl(string listen, string expected) => Assert.Equal(expected, StartupOptions.BrowserUrl(listen));
}
