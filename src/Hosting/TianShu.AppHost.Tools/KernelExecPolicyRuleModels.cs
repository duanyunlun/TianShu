namespace TianShu.AppHost.Tools;

/// <summary>
/// exec policy 规则匹配后的基础决策。
/// Base decision emitted by exec policy rule matching.
/// </summary>
internal enum KernelExecPolicyRuleDecision
{
    Allow,
    Ask,
    Deny,
}

/// <summary>
/// 面向命令前缀的 exec policy 规则。
/// Exec policy rule that targets a command prefix.
/// </summary>
internal sealed record KernelExecPolicyRule(
    KernelExecPolicyRuleDecision Decision,
    IReadOnlyList<string> CommandPrefix);

/// <summary>
/// 面向网络协议与主机的 exec policy 规则。
/// Exec policy rule that targets a protocol and host pair.
/// </summary>
internal sealed record KernelExecPolicyNetworkRule(
    KernelExecPolicyRuleDecision Decision,
    KernelManagedNetworkProtocol Protocol,
    string Host);
