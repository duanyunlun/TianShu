namespace TianShu.AppHost.Tools;

/// <summary>
/// 托管网络代理当前支持的协议类型。
/// Protocol kinds supported by the managed network proxy.
/// </summary>
internal enum KernelManagedNetworkProtocol
{
    Http,
    Https,
    Socks5Tcp,
    Socks5Udp,
}

/// <summary>
/// 托管网络策略修正时允许用户写回的动作。
/// Action that can be persisted as a managed network policy amendment.
/// </summary>
internal enum KernelManagedNetworkRuleAction
{
    Allow,
    Deny,
}
