using System.Net;
using System.Net.Sockets;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 托管网络宿主策略、blocked-response 与 host 归一化辅助方法。
/// Helper methods for managed network host normalization, policy messaging, and blocked responses.
/// </summary>
internal static class KernelManagedNetworkHelpers
{
    public static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var normalized = host.Trim();
        if (normalized.StartsWith('[') && normalized.EndsWith(']'))
        {
            normalized = normalized[1..^1];
        }

        return normalized.TrimEnd('.').ToLowerInvariant();
    }

    public static string ToPayloadProtocol(KernelManagedNetworkProtocol protocol)
    {
        return protocol switch
        {
            KernelManagedNetworkProtocol.Http => "http",
            KernelManagedNetworkProtocol.Https => "https",
            KernelManagedNetworkProtocol.Socks5Tcp => "socks5Tcp",
            KernelManagedNetworkProtocol.Socks5Udp => "socks5Udp",
            _ => "http",
        };
    }

    public static string ToProtocolLabel(KernelManagedNetworkProtocol protocol)
    {
        return protocol switch
        {
            KernelManagedNetworkProtocol.Http => "http",
            KernelManagedNetworkProtocol.Https => "https",
            KernelManagedNetworkProtocol.Socks5Tcp => "socks5-tcp",
            KernelManagedNetworkProtocol.Socks5Udp => "socks5-udp",
            _ => "http",
        };
    }

    public static bool IsFullMode(string? mode)
        => string.Equals(Normalize(mode), "full", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesDomainRule(string host, string rule)
    {
        var normalizedHost = NormalizeHost(host);
        var normalizedRule = NormalizeHost(rule);
        if (string.IsNullOrWhiteSpace(normalizedHost) || string.IsNullOrWhiteSpace(normalizedRule))
        {
            return false;
        }

        if (string.Equals(normalizedHost, normalizedRule, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var suffix = normalizedRule.TrimStart('*').TrimStart('.');
        return normalizedHost.EndsWith('.' + suffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLocalOrPrivateHost(string host)
    {
        var normalizedHost = NormalizeHost(host);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return false;
        }

        if (string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(normalizedHost, out var address))
        {
            return normalizedHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        var ipv6Bytes = address.GetAddressBytes();
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (ipv6Bytes.Length == 16 && (ipv6Bytes[0] & 0xFE) == 0xFC);
    }

    public static string BuildApprovalId(KernelManagedNetworkHostKey key)
        => $"network#{ToProtocolLabel(key.Protocol)}#{NormalizeHost(key.Host)}#{key.Port}";

    public static string BuildTarget(KernelManagedNetworkProtocol protocol, string host, int port)
        => $"{ToProtocolLabel(protocol)}://{host}:{port}";

    public static string ToPolicyProtocol(KernelManagedNetworkProtocol protocol)
    {
        return protocol switch
        {
            KernelManagedNetworkProtocol.Http => "http",
            KernelManagedNetworkProtocol.Https => "https_connect",
            KernelManagedNetworkProtocol.Socks5Tcp => "socks5_tcp",
            KernelManagedNetworkProtocol.Socks5Udp => "socks5_udp",
            _ => "http",
        };
    }

    public static string BuildPolicyDeniedMessage(KernelManagedNetworkProtocol protocol, string host, int port, string reason)
    {
        var target = BuildTarget(protocol, host, port);
        return reason switch
        {
            "denied" => $"Network access to \"{target}\" was blocked: domain is explicitly denied by policy and cannot be approved from this prompt.",
            "not_allowed" => $"Network access to \"{target}\" was blocked: domain is not in the allowlist.",
            "not_allowed_local" => $"Network access to \"{target}\" was blocked: local/private network addresses are blocked by policy.",
            "method_not_allowed" => $"Network access to \"{target}\" was blocked by method policy.",
            "mitm_required" => $"Network access to \"{target}\" was blocked: MITM required for limited HTTPS.",
            "approval_policy_never" => $"Network access to \"{target}\" was blocked by approval policy.",
            _ => $"Network access to \"{target}\" was blocked by policy.",
        };
    }

    public static string BuildBlockedHeaderValue(string reason)
    {
        return reason switch
        {
            "not_allowed" or "not_allowed_local" => "blocked-by-allowlist",
            "denied" => "blocked-by-denylist",
            "method_not_allowed" => "blocked-by-method-policy",
            "mitm_required" => "blocked-by-mitm-required",
            _ => "blocked-by-policy",
        };
    }

    public static string BuildBlockedResponseMessage(string reason)
    {
        return reason switch
        {
            "not_allowed" => "TianShu blocked this request: domain not in allowlist (this is not a denylist block).",
            "not_allowed_local" => "TianShu blocked this request: local/private addresses not allowed.",
            "denied" => "TianShu blocked this request: domain denied by policy.",
            "method_not_allowed" => "TianShu blocked this request: method not allowed in limited mode.",
            "mitm_required" => "TianShu blocked this request: MITM required for limited HTTPS.",
            _ => "TianShu blocked this request by network policy.",
        };
    }

    public static KernelManagedNetworkBlockedHttpPayload BuildBlockedHttpPayload(
        KernelManagedNetworkProtocol protocol,
        string host,
        int port,
        string reason,
        string source,
        string decision = "deny")
    {
        return new KernelManagedNetworkBlockedHttpPayload(
            Status: "blocked",
            Host: host,
            Reason: reason,
            Decision: decision,
            Source: source,
            Protocol: ToPolicyProtocol(protocol),
            Port: port,
            Message: BuildBlockedResponseMessage(reason));
    }

    public static KernelManagedNetworkBlockedHttpPayload BuildUnixSocketBlockedHttpPayload(string reason)
        => new(
            Status: "blocked",
            Host: "unix-socket",
            Reason: reason);

    public static KernelManagedNetworkOutcome CreatePolicyDeniedOutcome(
        KernelManagedNetworkProtocol protocol,
        string host,
        int port,
        string reason,
        string source = "baseline_policy",
        string decision = "deny")
    {
        return new KernelManagedNetworkOutcome(
            KernelManagedNetworkOutcomeKind.DeniedByPolicy,
            BuildPolicyDeniedMessage(protocol, host, port, reason),
            BuildBlockedHttpPayload(protocol, host, port, reason, source, decision));
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
