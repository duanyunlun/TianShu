using System.Net;

namespace TianShu.AppHost;

/// <summary>
/// 表示 AppHost 在本地启动时可选择的监听传输方式。
/// Represents the listen transport options available when AppHost starts locally.
/// </summary>
internal abstract record AppHostServerTransport
{
    public sealed record Stdio : AppHostServerTransport;

    public sealed record WebSocket(IPEndPoint BindAddress) : AppHostServerTransport;

    public static bool TryParse(string? listenUrl, out AppHostServerTransport transport, out string error)
    {
        if (string.IsNullOrWhiteSpace(listenUrl) || string.Equals(listenUrl, "stdio://", StringComparison.OrdinalIgnoreCase))
        {
            transport = new Stdio();
            error = string.Empty;
            return true;
        }

        if (listenUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != "ws"
                || string.IsNullOrWhiteSpace(uri.Host)
                || uri.Port <= 0)
            {
                transport = new Stdio();
                error = $"无效 --listen URL：{listenUrl}，应为 stdio:// 或 ws://IP:PORT。";
                return false;
            }

            if (!IPAddress.TryParse(uri.Host, out var address))
            {
                try
                {
                    address = Dns.GetHostAddresses(uri.Host).FirstOrDefault()
                              ?? throw new InvalidOperationException("host resolve failed");
                }
                catch
                {
                    transport = new Stdio();
                    error = $"无效 --listen URL：{listenUrl}，无法解析主机 {uri.Host}。";
                    return false;
                }
            }

            transport = new WebSocket(new IPEndPoint(address, uri.Port));
            error = string.Empty;
            return true;
        }

        transport = new Stdio();
        error = $"不支持的 --listen URL：{listenUrl}，应为 stdio:// 或 ws://IP:PORT。";
        return false;
    }
}
