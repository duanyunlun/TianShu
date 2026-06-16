using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 托管网络宿主侧 settings / approval / blocked-response 轻量载荷。
/// Lightweight host-side settings, approval, and blocked-response payloads for managed networking.
/// </summary>
internal enum KernelManagedNetworkOutcomeKind
{
    None,
    DeniedByUser,
    DeniedByPolicy,
}

internal sealed record KernelManagedNetworkSettings(
    bool RequirementsPresent,
    bool Enabled,
    string HttpHost,
    int HttpPort,
    string SocksHost,
    int SocksPort,
    bool EnableSocks5,
    bool EnableSocks5Udp,
    bool AllowUpstreamProxy,
    bool DangerouslyAllowNonLoopbackProxy,
    bool DangerouslyAllowAllUnixSockets,
    string Mode,
    IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<string> DeniedDomains,
    IReadOnlyList<string> AllowUnixSockets,
    bool AllowLocalBinding)
{
    public static KernelManagedNetworkSettings Disabled(bool requirementsPresent = false)
        => new(
            requirementsPresent,
            Enabled: false,
            HttpHost: "127.0.0.1",
            HttpPort: 0,
            SocksHost: "127.0.0.1",
            SocksPort: 0,
            EnableSocks5: false,
            EnableSocks5Udp: false,
            AllowUpstreamProxy: false,
            DangerouslyAllowNonLoopbackProxy: false,
            DangerouslyAllowAllUnixSockets: false,
            Mode: "limited",
            AllowedDomains: Array.Empty<string>(),
            DeniedDomains: Array.Empty<string>(),
            AllowUnixSockets: Array.Empty<string>(),
            AllowLocalBinding: false);

    public bool IsActive => RequirementsPresent && Enabled;
}

internal sealed record KernelManagedNetworkApprovalContext(string Host, KernelManagedNetworkProtocol Protocol)
{
    public object ToPayload() => new
    {
        host = Host,
        protocol = KernelManagedNetworkHelpers.ToPayloadProtocol(Protocol),
    };
}

internal sealed record KernelManagedNetworkPolicyAmendment(string Host, KernelManagedNetworkRuleAction Action)
{
    public object ToPayload() => new
    {
        host = Host,
        action = Action == KernelManagedNetworkRuleAction.Allow ? "allow" : "deny",
    };
}

internal sealed record KernelManagedNetworkApprovalRequest(
    string ThreadId,
    string TurnId,
    string ItemId,
    string ApprovalId,
    string Command,
    string Cwd,
    string Reason,
    KernelManagedNetworkApprovalContext NetworkApprovalContext,
    IReadOnlyList<KernelManagedNetworkPolicyAmendment> ProposedNetworkPolicyAmendments,
    IReadOnlyList<object?> AvailableDecisions);

internal sealed record KernelManagedNetworkApprovalResponse(
    string? Decision,
    KernelManagedNetworkPolicyAmendment? NetworkPolicyAmendment = null,
    bool ApplyProposedExecPolicyAmendment = false);

internal enum KernelManagedNetworkSideEffectKind
{
    DeveloperMessage,
    Warning,
}

internal sealed record KernelManagedNetworkSideEffect(KernelManagedNetworkSideEffectKind Kind, string Text);

internal enum KernelManagedNetworkPersistResultKind
{
    Persisted,
    HostMismatch,
    PersistFailed,
}

internal sealed record KernelManagedNetworkPersistResult(KernelManagedNetworkPersistResultKind Kind, string? ErrorMessage = null)
{
    public static KernelManagedNetworkPersistResult Success { get; } = new(KernelManagedNetworkPersistResultKind.Persisted);
}

internal sealed record KernelManagedNetworkBlockedHttpPayload(
    string Status,
    string Host,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Decision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Protocol = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Port = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);

internal sealed record KernelManagedNetworkBlockedRequest(
    string Host,
    string Reason,
    string? Client,
    string? Method,
    string? Mode,
    string Protocol,
    string? Decision,
    string? Source,
    int? Port,
    long Timestamp);

internal sealed record KernelManagedNetworkOutcome(
    KernelManagedNetworkOutcomeKind Kind,
    string? Message,
    KernelManagedNetworkBlockedHttpPayload? BlockedHttpPayload = null)
{
    public static KernelManagedNetworkOutcome None { get; } = new(KernelManagedNetworkOutcomeKind.None, null);
}

internal sealed record KernelManagedNetworkAuthorizationResult(bool Allowed, KernelManagedNetworkOutcome Outcome)
{
    public static KernelManagedNetworkAuthorizationResult Allow { get; } = new(true, KernelManagedNetworkOutcome.None);
}

internal readonly record struct KernelManagedNetworkHostKey(string Host, KernelManagedNetworkProtocol Protocol, int Port);
