using System.Net;
using TianShu.AppHost;

namespace TianShu.AppHost.Tests;

public sealed class AppHostTransportTests
{
    [Fact]
    public void TryParse_ShouldDefaultToStdio()
    {
        Assert.True(AppHostServerTransport.TryParse(null, out var transport, out var error));
        Assert.Empty(error);
        Assert.IsType<AppHostServerTransport.Stdio>(transport);
    }

    [Fact]
    public void TryParse_ShouldAcceptStdio()
    {
        Assert.True(AppHostServerTransport.TryParse("stdio://", out var transport, out var error));
        Assert.Empty(error);
        Assert.IsType<AppHostServerTransport.Stdio>(transport);
    }

    [Fact]
    public void TryParse_ShouldAcceptWebSocket()
    {
        Assert.True(AppHostServerTransport.TryParse("ws://127.0.0.1:3219", out var transport, out var error));
        Assert.Empty(error);
        var ws = Assert.IsType<AppHostServerTransport.WebSocket>(transport);
        Assert.Equal(IPAddress.Parse("127.0.0.1"), ws.BindAddress.Address);
        Assert.Equal(3219, ws.BindAddress.Port);
    }

    [Fact]
    public void TryParse_ShouldRejectUnsupportedScheme()
    {
        Assert.False(AppHostServerTransport.TryParse("tcp://127.0.0.1:1234", out _, out var error));
        Assert.Contains("不支持", error, StringComparison.Ordinal);
    }
}

